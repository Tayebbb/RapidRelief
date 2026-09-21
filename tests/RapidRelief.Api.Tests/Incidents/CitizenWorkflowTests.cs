using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RapidRelief.Api.Features.Incidents.Data;
using RapidRelief.Api.Features.Realtime.Data;
using RapidRelief.Api.Features.Relief.Data;
using RapidRelief.Api.Features.Rescue.Data;
using RapidRelief.Api.Features.Shelters.Data;
using RapidRelief.Api.Features.Shelters.Domain;
using RapidRelief.Api.Features.Shelters.Services;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Tests.Incidents;

/// <summary>
/// The citizen journey end to end: SOS → AI → verification → assignment → live stages → resolution,
/// plus relief requests, shelter suitability and offline replay.
/// </summary>
public sealed class CitizenWorkflowTests : IClassFixture<TestingWebAppFactory>
{
    private const string IncidentsPath = "/api/incidents";
    private const string RescuePath = "/api/rescue";
    private const string ReliefPath = "/api/relief/requests";

    private readonly TestingWebAppFactory _factory;

    public CitizenWorkflowTests(TestingWebAppFactory factory) => _factory = factory;

    private HttpClient Client(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(FakeAuthHandler.HeaderName, role);
        return client;
    }

    private async Task ResetAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var incidents = scope.ServiceProvider.GetRequiredService<IncidentsDbContext>();
        var rescue = scope.ServiceProvider.GetRequiredService<RescueDbContext>();
        var relief = scope.ServiceProvider.GetRequiredService<ReliefDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        await rescue.MissionLogs.ExecuteDeleteAsync();
        await rescue.Missions.ExecuteDeleteAsync();
        await rescue.TeamMembers.ExecuteDeleteAsync();
        await rescue.Teams.ExecuteDeleteAsync();
        await incidents.StatusHistory.ExecuteDeleteAsync();
        await incidents.Media.ExecuteDeleteAsync();
        await incidents.Reports.ExecuteDeleteAsync();
        await relief.Requests.ExecuteDeleteAsync();
        await notifications.Reads.ExecuteDeleteAsync();
        await notifications.Notifications.ExecuteDeleteAsync();
    }

    private static object Sos(string? key = null) => new
    {
        title = "SOS — immediate life risk",
        description = "Trapped on the roof with two children, water still rising.",
        disasterType = DisasterType.Flood,
        severity = Severity.Catastrophic,
        latitude = 23.8103,
        longitude = 90.4125,
        addressOrArea = "Sector 3, Riverside",
        affectedPeopleCount = 3,
        isSos = true,
        contactPhone = "+8801711234567",
        photoPaths = (string[]?)null,
        idempotencyKey = key,
    };

    private async Task<Guid> SendSosAsync(string? key = null)
    {
        var response = await Client(Roles.Citizen).PostAsJsonAsync(IncidentsPath, Sos(key));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<IncidentView>>();
        return envelope!.Data!.Id;
    }

    private async Task<IncidentView> ReadIncidentAsync(Guid id)
    {
        var envelope = await Client(Roles.Citizen).GetFromJsonAsync<ApiEnvelope<IncidentView>>($"{IncidentsPath}/{id}");
        return envelope!.Data!;
    }

    [Fact]
    public async Task Sos_reaches_resolution_and_the_citizen_can_follow_every_stage()
    {
        await ResetAsync();

        var incidentId = await SendSosAsync();

        // The citizen sees the report immediately, with a timestamped receipt entry.
        var afterSubmit = await ReadIncidentAsync(incidentId);
        Assert.True(afterSubmit.IsSos);
        Assert.Equal(IncidentStatus.Reported, afterSubmit.Status);
        Assert.Contains(afterSubmit.Timeline, x => x.Notes == "SOS report received");

        await Client(Roles.Government).PostAsJsonAsync($"{IncidentsPath}/{incidentId}/verify", new { approved = true });

        var assign = await Client(Roles.Rescuer).PostAsJsonAsync($"{RescuePath}/missions", new { incidentId });
        var mission = (await assign.Content.ReadFromJsonAsync<ApiEnvelope<MissionView>>())!.Data!;

        await Client(Roles.Rescuer).PostAsJsonAsync($"{RescuePath}/missions/{mission.Id}/status",
            new { status = MissionStatus.EnRoute, notes = "boat launched" });

        // En route and On scene share IncidentStatus.InProgress — the citizen must still see both.
        var enRoute = await ReadIncidentAsync(incidentId);
        Assert.Equal(IncidentStatus.InProgress, enRoute.Status);
        Assert.Equal("EnRoute", enRoute.MissionStage);
        Assert.Contains(enRoute.Timeline, x => x.Notes == "Mission EnRoute");

        await Client(Roles.Rescuer).PostAsJsonAsync($"{RescuePath}/missions/{mission.Id}/status",
            new { status = MissionStatus.OnScene, notes = "team at the building" });

        var onScene = await ReadIncidentAsync(incidentId);
        Assert.Equal("OnScene", onScene.MissionStage);
        Assert.Contains(onScene.Timeline, x => x.Notes == "Mission OnScene");

        await Client(Roles.Rescuer).PostAsJsonAsync($"{RescuePath}/missions/{mission.Id}/status",
            new { status = MissionStatus.Completed, notes = "all three evacuated" });

        var resolved = await ReadIncidentAsync(incidentId);
        Assert.Equal(IncidentStatus.Resolved, resolved.Status);
        Assert.NotNull(resolved.ResolvedAtUtc);

        // Six stages, each with its own timestamp, in order.
        var stages = resolved.Timeline.Select(x => x.Notes).ToList();
        Assert.Equal(
            ["SOS report received", "Verified by command centre", "Rescue team assigned", "Mission EnRoute", "Mission OnScene", "Mission Completed"],
            stages);
        Assert.True(resolved.Timeline.Zip(resolved.Timeline.Skip(1)).All(p => p.First.ChangedAtUtc <= p.Second.ChangedAtUtc));

        // Only actionable steps reach the inbox — triage noise is not pushed to the citizen.
        using var scope = _factory.Services.CreateScope();
        var citizenId = FakeAuthHandler.SeedUserIds[Roles.Citizen];
        var inbox = await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>()
            .Notifications.AsNoTracking().Where(x => x.UserId == citizenId).ToListAsync();

        Assert.Equal(5, inbox.Count);
        Assert.DoesNotContain(inbox, x => x.Summary.Contains("analysed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(inbox, x => x.Summary.Contains("verified", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(inbox, x => x.Summary.Contains("on the way", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(inbox, x => x.Summary.Contains("resolved", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_replayed_offline_report_is_delivered_exactly_once()
    {
        await ResetAsync();

        var first = await SendSosAsync("offline-sos-key");

        // The outbox retries with the same key after reconnecting.
        var replay = await Client(Roles.Citizen).PostAsJsonAsync(IncidentsPath, Sos("offline-sos-key"));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(first, (await replay.Content.ReadFromJsonAsync<ApiEnvelope<IncidentView>>())!.Data!.Id);

        using var scope = _factory.Services.CreateScope();
        var count = await scope.ServiceProvider.GetRequiredService<IncidentsDbContext>().Reports.AsNoTracking().CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Relief_request_runs_from_requested_to_delivered_and_notifies_the_citizen()
    {
        await ResetAsync();

        var create = await Client(Roles.Citizen).PostAsJsonAsync(ReliefPath, new
        {
            type = ResourceType.Water,
            quantity = 4,
            recipientCount = 6,
            urgency = "Critical",
            latitude = 23.8103,
            longitude = 90.4125,
            deliveryAddress = "Block C, second floor",
            notes = "One infant needs formula.",
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var request = (await create.Content.ReadFromJsonAsync<ApiEnvelope<ReliefView>>())!.Data!;
        Assert.Equal(ReliefStatus.Pending, request.Status);

        foreach (var next in new[] { ReliefStatus.Approved, ReliefStatus.Allocated, ReliefStatus.Dispatched, ReliefStatus.Delivered })
        {
            var step = await Client(Roles.Government).PostAsJsonAsync($"{ReliefPath}/{request.Id}/status", new { status = next });
            Assert.Equal(HttpStatusCode.OK, step.StatusCode);
        }

        var mine = await Client(Roles.Citizen).GetFromJsonAsync<ApiEnvelope<PagedResult<ReliefView>>>($"{ReliefPath}/mine");
        Assert.Equal(ReliefStatus.Delivered, mine!.Data!.Items.Single().Status);

        using var scope = _factory.Services.CreateScope();
        var citizenId = FakeAuthHandler.SeedUserIds[Roles.Citizen];
        var inbox = await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>()
            .Notifications.AsNoTracking().Where(x => x.UserId == citizenId).ToListAsync();

        Assert.Equal(4, inbox.Count);
        Assert.Contains(inbox, x => x.Summary.Contains("accepted", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(inbox, x => x.Summary.Contains("delivered", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Relief_requests_are_private_validated_and_cancellable_only_while_early()
    {
        await ResetAsync();

        var invalid = await Client(Roles.Citizen).PostAsJsonAsync(ReliefPath, new
        {
            type = ResourceType.Water,
            quantity = 0,
            recipientCount = 0,
            latitude = 500.0,
            longitude = 90.4125,
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var create = await Client(Roles.Citizen).PostAsJsonAsync(ReliefPath, new
        {
            type = ResourceType.Food,
            quantity = 2,
            recipientCount = 3,
            latitude = 23.81,
            longitude = 90.41,
        });
        var request = (await create.Content.ReadFromJsonAsync<ApiEnvelope<ReliefView>>())!.Data!;

        // Another citizen role cannot reach the triage queue at all.
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(Roles.Citizen).GetAsync(ReliefPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Client(Roles.Rescuer).PostAsJsonAsync($"{ReliefPath}/{request.Id}/status", new { status = ReliefStatus.Approved })).StatusCode);

        // Skipping a stage is refused.
        Assert.Equal(HttpStatusCode.Conflict,
            (await Client(Roles.Government).PostAsJsonAsync($"{ReliefPath}/{request.Id}/status", new { status = ReliefStatus.Delivered })).StatusCode);

        // The citizen may cancel while it is still early.
        var cancel = await Client(Roles.Citizen).PostAsync($"{ReliefPath}/{request.Id}/cancel", content: null);
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal(ReliefStatus.Rejected, (await cancel.Content.ReadFromJsonAsync<ApiEnvelope<ReliefView>>())!.Data!.Status);
    }

    [Fact]
    public async Task Shelter_recommendations_prefer_space_over_raw_distance()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpsDbContext>();
            await db.Shelters.ExecuteDeleteAsync();
            db.Shelters.AddRange(
                new Shelter
                {
                    Id = Guid.NewGuid(),
                    Name = "Closest but nearly full",
                    Location = new GeoPoint(23.8110, 90.4130),
                    Capacity = 100,
                    CurrentOccupancy = 99,
                    Facilities = ["Water"],
                    Status = ShelterStatus.Open,
                },
                new Shelter
                {
                    Id = Guid.NewGuid(),
                    Name = "Slightly further with room",
                    Location = new GeoPoint(23.8180, 90.4200),
                    Capacity = 300,
                    CurrentOccupancy = 60,
                    Facilities = ["Water", "Medical", "Food"],
                    Status = ShelterStatus.Open,
                },
                new Shelter
                {
                    Id = Guid.NewGuid(),
                    Name = "Full",
                    Location = new GeoPoint(23.8104, 90.4126),
                    Capacity = 50,
                    CurrentOccupancy = 50,
                    Facilities = ["Water"],
                    Status = ShelterStatus.Open,
                },
                new Shelter
                {
                    Id = Guid.NewGuid(),
                    Name = "Closed",
                    Location = new GeoPoint(23.8105, 90.4127),
                    Capacity = 500,
                    CurrentOccupancy = 0,
                    Facilities = ["Water"],
                    Status = ShelterStatus.Closed,
                });
            await db.SaveChangesAsync();
        }

        var recommendations = await _factory.CreateClient()
            .GetFromJsonAsync<ApiEnvelope<List<RecommendationView>>>("/api/shelters/recommendations?lat=23.8103&lng=90.4125&count=3");

        var items = recommendations!.Data!;
        Assert.Equal("Slightly further with room", items[0].Name);
        Assert.All(items, x => Assert.True(x.FreeSpaces > 0));
        Assert.DoesNotContain(items, x => x.Name is "Full" or "Closed");
        Assert.Contains(items[0].Reasons, r => r.Contains("spaces left", StringComparison.OrdinalIgnoreCase));

        // Restore the shared demo dataset for the other suites in this fixture.
        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<OpsDbContext>().Shelters.ExecuteDeleteAsync();
            await ShelterSeeder.SeedAsync(scope.ServiceProvider, CancellationToken.None);
        }
    }

    private sealed record IncidentView(
        Guid Id,
        IncidentStatus Status,
        bool IsSos,
        string? MissionStage,
        DateTimeOffset? ResolvedAtUtc,
        IReadOnlyList<TimelineView> Timeline);

    private sealed record TimelineView(string Notes, DateTimeOffset ChangedAtUtc);

    private sealed record MissionView(Guid Id, MissionStatus Status);

    private sealed record ReliefView(Guid Id, ResourceType Type, ReliefStatus Status);

    private sealed record RecommendationView(Guid Id, string Name, int FreeSpaces, double DistanceKm, IReadOnlyList<string> Reasons);
}
