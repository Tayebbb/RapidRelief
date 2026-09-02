using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RapidRelief.Api.Features.Realtime;
using RapidRelief.Api.Features.Realtime.Data;
using RapidRelief.Api.Features.Realtime.Domain;
using RapidRelief.Api.Features.Realtime.Endpoints;
using RapidRelief.Api.Features.Realtime.Handlers;
using RapidRelief.Api.Features.Realtime.Hubs;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Events;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Tests.Realtime;

/// <summary>
/// D-033/D-036/D-037 — the notifier persists then pushes, drops oversized payloads, and NEVER
/// throws back at the publisher (the bus runs handlers inline in the publisher's request).
/// </summary>
public sealed class NotifierTests : IClassFixture<TestingWebAppFactory>
{
    private readonly TestingWebAppFactory _factory;

    public NotifierTests(TestingWebAppFactory factory) => _factory = factory;

    private IRealtimeNotifier Notifier => _factory.Services.GetRequiredService<IRealtimeNotifier>();

    private async Task<Notification?> SingleForTopicAsync(string topic)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        return await db.Notifications.AsNoTracking().SingleOrDefaultAsync(n => n.Topic == topic);
    }

    private async Task<List<Notification>> ForTopicAsync(string topic)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        return await db.Notifications.AsNoTracking().Where(n => n.Topic == topic).ToListAsync();
    }

    private static string UniqueTopic() => $"test.notifier.{Guid.NewGuid():N}";

    [Fact]
    public async Task Notify_all_persists_an_all_audience_row()
    {
        var topic = UniqueTopic();

        await Notifier.NotifyAllAsync(topic, new { Title = "Everyone" });

        var row = await SingleForTopicAsync(topic);
        Assert.NotNull(row);
        Assert.Equal(NotificationAudience.All, row!.Audience);
        Assert.Null(row.Role);
        Assert.Null(row.UserId);
        Assert.Equal("Everyone", row.Summary);
        Assert.Contains("Everyone", row.PayloadJson, StringComparison.Ordinal);
        Assert.NotEqual(default, row.CreatedAtUtc);
    }

    [Fact]
    public async Task Notify_role_persists_a_role_audience_row()
    {
        var topic = UniqueTopic();

        await Notifier.NotifyRoleAsync(Roles.Rescue, topic, new { Summary = "Rescue only" });

        var row = await SingleForTopicAsync(topic);
        Assert.NotNull(row);
        Assert.Equal(NotificationAudience.Role, row!.Audience);
        Assert.Equal(Roles.Rescue, row.Role);
        Assert.Null(row.UserId);
        Assert.Equal("Rescue only", row.Summary);
    }

    [Fact]
    public async Task Notify_user_persists_a_user_audience_row()
    {
        var topic = UniqueTopic();
        var userId = Guid.NewGuid();

        await Notifier.NotifyUserAsync(userId, topic, new { Title = "Just you" });

        var row = await SingleForTopicAsync(topic);
        Assert.NotNull(row);
        Assert.Equal(NotificationAudience.User, row!.Audience);
        Assert.Null(row.Role);
        Assert.Equal(userId, row.UserId);
    }

    [Fact]
    public async Task Payload_over_the_cap_is_dropped_and_no_row_is_written()
    {
        var topic = UniqueTopic();
        var oversized = new string('x', Notification.MaxPayloadChars + 1);

        await Notifier.NotifyAllAsync(topic, new { Title = "Too big", Body = oversized });

        Assert.Empty(await ForTopicAsync(topic));
    }

    [Fact]
    public async Task Payload_at_the_cap_is_still_persisted()
    {
        var topic = UniqueTopic();
        // {"Body":"…"} — 11 characters of JSON scaffolding around the value.
        var body = new string('x', Notification.MaxPayloadChars - 11);

        await Notifier.NotifyAllAsync(topic, new { Body = body });

        var row = await SingleForTopicAsync(topic);
        Assert.NotNull(row);
        Assert.Equal(Notification.MaxPayloadChars, row!.PayloadJson.Length);
    }

    [Fact]
    public async Task Summary_falls_back_to_the_topic_when_the_payload_has_no_title_or_summary()
    {
        var topic = UniqueTopic();

        await Notifier.NotifyAllAsync(topic, new { Count = 3 });

        var row = await SingleForTopicAsync(topic);
        Assert.Equal(topic, row!.Summary);
    }

    [Fact]
    public async Task Summary_prefers_title_over_summary_and_is_clamped_and_control_stripped()
    {
        var topic = UniqueTopic();
        var noisy = "Line\u0001one\nbroken " + new string('t', 200);

        await Notifier.NotifyAllAsync(topic, new { Title = noisy, Summary = "ignored" });

        var row = await SingleForTopicAsync(topic);
        Assert.Equal(Notification.MaxSummaryChars, row!.Summary.Length);
        Assert.DoesNotContain('\u0001', row.Summary);
        Assert.DoesNotContain('\n', row.Summary);
        Assert.StartsWith("Line one broken", row.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Topic_is_sanitised_to_the_documented_alphabet_and_length()
    {
        var marker = Guid.NewGuid().ToString("N");

        await Notifier.NotifyAllAsync($"AI.Incident Assessed!{marker}", new { Title = "x" });

        var expected = $"ai.incidentassessed{marker}";
        var row = await SingleForTopicAsync(expected);
        Assert.NotNull(row);
    }

    [Fact]
    public async Task Overlong_topics_are_truncated_to_64_characters()
    {
        var prefix = Guid.NewGuid().ToString("N"); // 32 chars
        var topic = prefix + new string('a', 100);

        await Notifier.NotifyAllAsync(topic, new { Title = "x" });

        var rows = await ForTopicAsync((prefix + new string('a', 100))[..Notification.MaxTopicChars]);
        Assert.Single(rows);
    }

    [Fact]
    public async Task Degraded_database_skips_persistence_without_throwing()
    {
        var topic = UniqueTopic();
        var health = _factory.Services.GetRequiredService<DatabaseHealth>();
        try
        {
            health.PostgresAvailable = false;

            await Notifier.NotifyAllAsync(topic, new { Title = "degraded" });
        }
        finally
        {
            health.PostgresAvailable = true;
        }

        Assert.Empty(await ForTopicAsync(topic));
    }

    [Fact]
    public async Task Incident_assessed_event_fans_out_to_rescue_and_admin()
    {
        var incidentId = Guid.NewGuid();

        await RealtimeTestSupport.PublishAsync(_factory.Services,
            new IncidentAssessed(incidentId, Severity.Severe, 88, "Flooding on Mirpur Road", null));

        var rows = await ForTopicAsync(IncidentAssessedNotificationHandler.Topic);
        var mine = rows.Where(r => r.PayloadJson.Contains(incidentId.ToString(), StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal(2, mine.Count);
        Assert.All(mine, r => Assert.Equal(NotificationAudience.Role, r.Audience));
        Assert.Contains(mine, r => r.Role == Roles.Rescue);
        Assert.Contains(mine, r => r.Role == Roles.Admin);
        Assert.All(mine, r => Assert.Equal("Flooding on Mirpur Road", r.Summary));
    }

    [Fact]
    public async Task Alert_published_event_fans_out_to_everyone()
    {
        var alertId = Guid.NewGuid();

        await RealtimeTestSupport.PublishAsync(_factory.Services,
            new AlertPublished(alertId, "Cyclone warning", "Move to shelters", Severity.Catastrophic,
                DisasterType.Cyclone, DateTimeOffset.UtcNow.AddHours(6)));

        var rows = await ForTopicAsync(AlertPublishedNotificationHandler.Topic);
        var mine = rows.Where(r => r.PayloadJson.Contains(alertId.ToString(), StringComparison.OrdinalIgnoreCase)).ToList();
        var row = Assert.Single(mine);
        Assert.Equal(NotificationAudience.All, row.Audience);
        Assert.Equal("Cyclone warning", row.Summary);
    }

    [Fact]
    public async Task A_failing_hub_never_surfaces_to_the_publisher_and_the_row_is_still_written()
    {
        using var host = new IsolatedNotifierHost(hubContext: new ThrowingHubContext());

        await host.Notifier.NotifyAllAsync("hub.explodes", new { Title = "still stored" });

        Assert.Equal(1, await host.CountAsync());
    }

    [Fact]
    public async Task A_failing_database_never_surfaces_to_the_publisher()
    {
        using var host = new IsolatedNotifierHost(hubContext: null, createSchema: false);

        var thrown = await Record.ExceptionAsync(() =>
            host.Notifier.NotifyAllAsync("db.explodes", new { Title = "no table here" }));

        Assert.Null(thrown);
    }

    [Fact]
    public async Task A_failing_persist_still_delivers_the_live_push()
    {
        var hub = new RecordingHubContext();
        using var host = new IsolatedNotifierHost(hub, createSchema: false);

        await host.Notifier.NotifyAllAsync("persist.explodes", new { Title = "live anyway" });

        var (method, argument) = Assert.Single(hub.Sends);
        Assert.Equal(NotificationsHub.MethodName, method);
        Assert.Equal("persist.explodes", argument.Topic);
    }

    [Fact]
    public async Task Unserialisable_payloads_never_surface_to_the_publisher()
    {
        var topic = UniqueTopic();

        var thrown = await Record.ExceptionAsync(() => Notifier.NotifyAllAsync(topic, new SelfReferencing()));

        Assert.Null(thrown);
        Assert.Empty(await ForTopicAsync(topic));
    }

    private sealed class SelfReferencing
    {
        public SelfReferencing() => Self = this;

        public SelfReferencing Self { get; }
    }
}

/// <summary>Notifier wired against its own SQLite store so DB/hub faults can be forced.</summary>
internal sealed class IsolatedNotifierHost : IDisposable
{
    private readonly SqliteConnection _anchor;
    private readonly ServiceProvider _services;

    public IsolatedNotifierHost(IHubContext<NotificationsHub>? hubContext, bool createSchema = true)
    {
        var connectionString = $"Data Source=notifier-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False";
        _anchor = new SqliteConnection(connectionString);
        _anchor.Open();

        var services = new ServiceCollection();
        services.AddDbContext<NotificationsDbContext>(o => o.UseSqlite(connectionString));
        services.AddSingleton(new DatabaseHealth { PostgresAvailable = true });
        _services = services.BuildServiceProvider();

        if (createSchema)
        {
            using var scope = _services.CreateScope();
            scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Database.EnsureCreated();
        }

        Notifier = new SignalRRealtimeNotifier(
            _services.GetRequiredService<IServiceScopeFactory>(),
            hubContext,
            TimeProvider.System,
            NullLogger<SignalRRealtimeNotifier>.Instance);
    }

    public IRealtimeNotifier Notifier { get; }

    public async Task<int> CountAsync()
    {
        using var scope = _services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Notifications.CountAsync();
    }

    public void Dispose()
    {
        _services.Dispose();
        _anchor.Dispose();
    }
}

internal sealed class ThrowingHubContext : IHubContext<NotificationsHub>
{
    public IHubClients Clients => throw new InvalidOperationException("hub is down");

    public IGroupManager Groups => throw new InvalidOperationException("hub is down");
}

/// <summary>Captures what the notifier pushed so a push can be asserted without a live socket.</summary>
internal sealed class RecordingHubContext : IHubContext<NotificationsHub>
{
    private readonly RecordingClientProxy _proxy;

    public RecordingHubContext() => _proxy = new RecordingClientProxy(this);

    public List<(string Method, NotificationDto Argument)> Sends { get; } = [];

    public IHubClients Clients => new RecordingClients(_proxy);

    public IGroupManager Groups => throw new NotSupportedException();

    private sealed class RecordingClientProxy : IClientProxy
    {
        private readonly RecordingHubContext _owner;

        public RecordingClientProxy(RecordingHubContext owner) => _owner = owner;

        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            _owner.Sends.Add((method, (NotificationDto)args[0]!));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingClients : IHubClients
    {
        private readonly IClientProxy _proxy;

        public RecordingClients(IClientProxy proxy) => _proxy = proxy;

        public IClientProxy All => _proxy;

        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => _proxy;

        public IClientProxy Client(string connectionId) => _proxy;

        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => _proxy;

        public IClientProxy Group(string groupName) => _proxy;

        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => _proxy;

        public IClientProxy Groups(IReadOnlyList<string> groupNames) => _proxy;

        public IClientProxy User(string userId) => _proxy;

        public IClientProxy Users(IReadOnlyList<string> userIds) => _proxy;
    }
}
