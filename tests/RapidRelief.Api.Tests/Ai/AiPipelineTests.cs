using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RapidRelief.Api.Features.Ai.Data;
using RapidRelief.Api.Features.Ai.Domain;
using RapidRelief.Api.Features.Ai.Pipeline;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Eventing;
using RapidRelief.Shared.Contracts.Events;
using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Api.Tests.Ai;

/// <summary>Captures every IncidentAssessed the pipeline publishes (registered as a scoped probe handler).</summary>
public sealed class IncidentAssessedProbe
{
    public ConcurrentQueue<IncidentAssessed> Events { get; } = new();
}

public sealed class IncidentAssessedProbeHandler(IncidentAssessedProbe probe) : IEventHandler<IncidentAssessed>
{
    public Task HandleAsync(IncidentAssessed evt, CancellationToken ct = default)
    {
        probe.Events.Enqueue(evt);
        return Task.CompletedTask;
    }
}

/// <summary>Boots the real factory plus a derived host that adds the IncidentAssessed probe.</summary>
public sealed class AiPipelineFixture : IDisposable
{
    public TestingWebAppFactory Root { get; } = new();

    public WebApplicationFactory<Program> Factory { get; }

    public IncidentAssessedProbe Probe { get; } = new();

    public AiPipelineFixture()
    {
        Factory = Root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddSingleton(Probe);
            services.AddScoped<IEventHandler<IncidentAssessed>, IncidentAssessedProbeHandler>();
        }));
    }

    public void Dispose()
    {
        Factory.Dispose();
        Root.Dispose();
    }
}

/// <summary>
/// F8 blueprint TEST PLAN item 1 / D-021: IncidentCreated → bounded channel → worker →
/// persisted row + IncidentAssessed; redelivery idempotent; degraded DB analyzes+publishes
/// but skips persist (D-028); DropWrite channel logs dropped items.
/// </summary>
public sealed class AiPipelineTests : IClassFixture<AiPipelineFixture>
{
    private static readonly TimeSpan PollDeadline = TimeSpan.FromSeconds(15);

    private readonly AiPipelineFixture _fixture;

    public AiPipelineTests(AiPipelineFixture fixture) => _fixture = fixture;

    private static IncidentCreated Evt(
        Guid incidentId,
        double lat = 23.8103,
        double lon = 90.4125,
        DisasterType type = DisasterType.Flood,
        string description = "water rising in the street",
        bool isSos = false)
        => new(incidentId, Guid.NewGuid(), type, Severity.Moderate,
            new GeoPoint(lat, lon), description, isSos, Array.Empty<string>());

    private async Task PublishAsync(IncidentCreated evt)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IEventBus>().PublishAsync(evt);
    }

    private async Task<AiAssessment?> WaitForAssessmentAsync(Guid incidentId, TimeSpan? deadline = null)
    {
        var stopAt = DateTime.UtcNow + (deadline ?? PollDeadline);
        while (DateTime.UtcNow < stopAt)
        {
            using var scope = _fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();
            var row = await db.Assessments.AsNoTracking()
                .FirstOrDefaultAsync(a => a.IncidentId == incidentId);
            if (row is not null)
            {
                return row;
            }
            await Task.Delay(50);
        }
        return null;
    }

    private async Task<IncidentAssessed?> WaitForAssessedEventAsync(Guid incidentId, TimeSpan? deadline = null)
    {
        var stopAt = DateTime.UtcNow + (deadline ?? PollDeadline);
        while (DateTime.UtcNow < stopAt)
        {
            var evt = _fixture.Probe.Events.FirstOrDefault(e => e.IncidentId == incidentId);
            if (evt is not null)
            {
                return evt;
            }
            await Task.Delay(50);
        }
        return null;
    }

    [Fact]
    public async Task Incident_created_produces_persisted_assessment_and_assessed_event()
    {
        var incidentId = Guid.NewGuid();
        var evt = Evt(incidentId, isSos: true);

        await PublishAsync(evt);

        var row = await WaitForAssessmentAsync(incidentId);
        Assert.NotNull(row);
        Assert.Equal(incidentId, row!.IncidentId);
        Assert.Equal("RuleBased", row.Provider); // no API key + placeholder client → fallback
        Assert.Equal(evt.Location.Latitude, row.SnapshotLatitude);
        Assert.Equal(evt.Location.Longitude, row.SnapshotLongitude);
        Assert.Equal(evt.Type, row.SnapshotType);
        Assert.Equal(evt.OccurredAtUtc, row.SnapshotReportedAtUtc);
        Assert.True(row.SnapshotIsSos);
        Assert.InRange(row.PriorityScore, 0, 100);
        Assert.NotEmpty(row.Summary);
        Assert.True(row.Summary.Length <= 200);

        var assessed = await WaitForAssessedEventAsync(incidentId);
        Assert.NotNull(assessed);
        Assert.Equal(row.EstimatedSeverity, assessed!.EstimatedSeverity);
        Assert.Equal(row.PriorityScore, assessed.PriorityScore);
        Assert.Equal(row.Summary, assessed.Summary);
        Assert.Equal(row.PossibleDuplicateOfId, assessed.PossibleDuplicateOfId);
    }

    [Fact]
    public async Task Redelivery_of_the_same_incident_keeps_one_row_and_one_publish()
    {
        var incidentId = Guid.NewGuid();
        var evt = Evt(incidentId);

        await PublishAsync(evt);
        Assert.NotNull(await WaitForAssessmentAsync(incidentId));
        Assert.NotNull(await WaitForAssessedEventAsync(incidentId));

        await PublishAsync(evt); // redelivery
        // Absence proof needs a fixed window: 1.5 s comfortably exceeds the worker's dequeue+
        // analyze+persist time for one item (rule-based path is ~ms), so a wrong second
        // processing would have landed before we count.
        await Task.Delay(1500);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();
        Assert.Equal(1, await db.Assessments.CountAsync(a => a.IncidentId == incidentId));
        Assert.Equal(1, _fixture.Probe.Events.Count(e => e.IncidentId == incidentId));
    }

    [Fact]
    public async Task Near_duplicate_incident_links_to_the_nearest_earlier_assessment()
    {
        // Seeded-pair analog: same Mirpur block, same declared type, seconds apart.
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        await PublishAsync(Evt(firstId, lat: 23.8225, lon: 90.3652));
        Assert.NotNull(await WaitForAssessmentAsync(firstId));

        await PublishAsync(Evt(secondId, lat: 23.8235, lon: 90.3660));
        var second = await WaitForAssessmentAsync(secondId);

        Assert.NotNull(second);
        Assert.Equal(firstId, second!.PossibleDuplicateOfId);

        var assessed = await WaitForAssessedEventAsync(secondId);
        Assert.Equal(firstId, assessed!.PossibleDuplicateOfId);
    }

    [Fact]
    public async Task Different_type_neighbour_is_not_linked()
    {
        var floodId = Guid.NewGuid();
        var fireId = Guid.NewGuid();

        await PublishAsync(Evt(floodId, lat: 23.7101, lon: 90.3720, type: DisasterType.Flood));
        Assert.NotNull(await WaitForAssessmentAsync(floodId));

        await PublishAsync(Evt(fireId, lat: 23.7101, lon: 90.3721, type: DisasterType.Fire,
            description: "smoke over the market"));
        var fire = await WaitForAssessmentAsync(fireId);

        Assert.NotNull(fire);
        Assert.Null(fire!.PossibleDuplicateOfId);
    }

    [Fact]
    public async Task Degraded_database_still_publishes_assessed_event_but_skips_persist()
    {
        var health = _fixture.Factory.Services.GetRequiredService<DatabaseHealth>();
        var incidentId = Guid.NewGuid();
        try
        {
            health.PostgresAvailable = false;

            await PublishAsync(Evt(incidentId));

            var assessed = await WaitForAssessedEventAsync(incidentId);
            Assert.NotNull(assessed); // analysis + publish survive the outage (D-028)
        }
        finally
        {
            health.PostgresAvailable = true;
        }

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();
        Assert.False(await db.Assessments.AnyAsync(a => a.IncidentId == incidentId));
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (Entries)
            {
                Entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }

    [Fact]
    public void Full_channel_drops_the_newest_item_and_logs_an_error()
    {
        var logger = new CapturingLogger();
        var channel = AiChannel.Create(capacity: 1, logger);
        var keep = new AiWorkItem(new AiAnalysisRequest(
            Guid.NewGuid(), DisasterType.Flood, "kept", new GeoPoint(0, 0), false,
            DateTimeOffset.UtcNow, Array.Empty<string>()));
        var dropped = new AiWorkItem(new AiAnalysisRequest(
            Guid.NewGuid(), DisasterType.Fire, "dropped", new GeoPoint(0, 0), false,
            DateTimeOffset.UtcNow, Array.Empty<string>()));

        Assert.True(channel.Writer.TryWrite(keep));
        channel.Writer.TryWrite(dropped); // DropWrite: silently discarded by the channel

        Assert.Equal(1, channel.Reader.Count);
        Assert.True(channel.Reader.TryRead(out var read));
        Assert.Equal(keep, read);
        var errors = logger.Entries.Where(e => e.Level == LogLevel.Error).ToList();
        Assert.Single(errors);
        Assert.Contains(dropped.Request.IncidentId.ToString(), errors[0].Message);
    }
}
