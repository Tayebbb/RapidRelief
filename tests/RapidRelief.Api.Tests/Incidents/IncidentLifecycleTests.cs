using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RapidRelief.Api.Features.Incidents.Data;
using RapidRelief.Api.Features.Realtime.Data;
using RapidRelief.Api.Features.Rescue.Data;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Tests.Incidents;

/// <summary>
/// Citizen → incident → AI assessment → priority → rescue assignment → mission → resolution →
/// citizen notification, exercised over real HTTP against the real composition.
/// </summary>
public sealed class IncidentLifecycleTests : IClassFixture<TestingWebAppFactory>
{
    private const string IncidentsPath = "/api/incidents";
    private const string RescuePath = "/api/rescue";
    private readonly TestingWebAppFactory _factory;

    public IncidentLifecycleTests(TestingWebAppFactory factory) => _factory = factory;

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
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        await rescue.MissionLogs.ExecuteDeleteAsync();
        await rescue.Missions.ExecuteDeleteAsync();
        await rescue.TeamMembers.ExecuteDeleteAsync();
        await rescue.Teams.ExecuteDeleteAsync();
        await incidents.StatusHistory.ExecuteDeleteAsync();
        await incidents.Media.ExecuteDeleteAsync();
        await incidents.Reports.ExecuteDeleteAsync();
        await notifications.Reads.ExecuteDeleteAsync();
        await notifications.Notifications.ExecuteDeleteAsync();
    }

    private static object NewReport(bool sos = false, string? key = null) => new
    {
        title = "Water rising around block 4",
        description = "Ground floor flooded to waist height; five residents on the roof.",
        disasterType = DisasterType.Flood,
        severity = Severity.Severe,
        latitude = 23.8103,
        longitude = 90.4125,
        addressOrArea = "Sector 3, Riverside",
        affectedPeopleCount = 5,
        isSos = sos,
        contactPhone = "+8801711234567",
        photoPaths = (string[]?)null,
        idempotencyKey = key,
    };

    private async Task<Guid> ReportAsync(bool sos = false, string? key = null)
    {
        var response = await Client(Roles.Citizen).PostAsJsonAsync(IncidentsPath, NewReport(sos, key));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<IncidentView>>();
        Assert.NotNull(envelope?.Data);
        return envelope!.Data!.Id;
    }

    private async Task<IncidentStatus> StatusAsync(Guid incidentId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IncidentsDbContext>();
        return await db.Reports.AsNoTracking().Where(x => x.Id == incidentId).Select(x => x.Status).SingleAsync();
    }

    private async Task<bool> WaitForAsync(Func<Task<bool>> condition, TimeSpan? deadline = null)
    {
        var until = DateTimeOffset.UtcNow + (deadline ?? TimeSpan.FromSeconds(10));
        while (DateTimeOffset.UtcNow < until)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return await condition();
    }

    [Fact]
    public async Task Full_loop_report_to_resolution_updates_status_and_notifies_the_citizen()
    {
        await ResetAsync();

        // 1. Citizen reports.
        var incidentId = await ReportAsync();
        Assert.Equal(IncidentStatus.Reported, await StatusAsync(incidentId));

        // 2. The AI pipeline picks it up from IncidentCreated and projects a priority score.
        var assessed = await WaitForAsync(async () =>
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IncidentsDbContext>();
            return await db.Reports.AsNoTracking().AnyAsync(x => x.Id == incidentId && x.PriorityScore != null);
        });
        Assert.True(assessed, "The AI pipeline never projected a priority score onto the incident.");

        // 3. Government verifies.
        var verify = await Client(Roles.Government).PostAsJsonAsync($"{IncidentsPath}/{incidentId}/verify",
            new { approved = true, reason = (string?)null });
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        Assert.Equal(IncidentStatus.Verified, await StatusAsync(incidentId));

        // 4. It reaches the rescue queue.
        var queue = await Client(Roles.Rescuer).GetFromJsonAsync<ApiEnvelope<PagedResult<QueueView>>>($"{RescuePath}/queue");
        Assert.Contains(queue!.Data!.Items, x => x.IncidentId == incidentId);

        // 5. A rescuer accepts it — a personal team is provisioned on the fly.
        var assign = await Client(Roles.Rescuer).PostAsJsonAsync($"{RescuePath}/missions",
            new { incidentId, teamId = (Guid?)null, missionTitle = (string?)null, priority = (string?)null });
        Assert.Equal(HttpStatusCode.Created, assign.StatusCode);
        var mission = (await assign.Content.ReadFromJsonAsync<ApiEnvelope<MissionView>>())!.Data!;
        Assert.Equal(MissionStatus.Assigned, mission.Status);
        Assert.Equal(IncidentStatus.Assigned, await StatusAsync(incidentId));

        // 6. Mission progresses through its legal transitions.
        foreach (var next in new[] { MissionStatus.EnRoute, MissionStatus.OnScene, MissionStatus.Completed })
        {
            var step = await Client(Roles.Rescuer).PostAsJsonAsync($"{RescuePath}/missions/{mission.Id}/status",
                new { status = next, notes = "field update" });
            Assert.Equal(HttpStatusCode.OK, step.StatusCode);
        }

        // 7. Resolution lands on the incident and the citizen has been notified along the way.
        Assert.Equal(IncidentStatus.Resolved, await StatusAsync(incidentId));

        using var scope = _factory.Services.CreateScope();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var citizenId = FakeAuthHandler.SeedUserIds[Roles.Citizen];
        var forCitizen = await notifications.Notifications.AsNoTracking()
            .Where(x => x.UserId == citizenId)
            .ToListAsync();

        Assert.NotEmpty(forCitizen);
        Assert.Contains(forCitizen, n => n.Summary.Contains("resolved", StringComparison.OrdinalIgnoreCase));

        var timeline = await scope.ServiceProvider.GetRequiredService<IncidentsDbContext>()
            .StatusHistory.AsNoTracking().Where(x => x.IncidentId == incidentId).ToListAsync();
        Assert.Contains(timeline, x => x.ToStatus == IncidentStatus.Verified);
        Assert.Contains(timeline, x => x.ToStatus == IncidentStatus.Assigned);
        Assert.Contains(timeline, x => x.ToStatus == IncidentStatus.Resolved);
    }

    [Fact]
    public async Task Sos_reports_sort_to_the_front_of_the_rescue_queue()
    {
        await ResetAsync();
        await ReportAsync();
        var sosId = await ReportAsync(sos: true, key: "sos-key");

        var queue = await Client(Roles.Rescuer).GetFromJsonAsync<ApiEnvelope<PagedResult<QueueView>>>($"{RescuePath}/queue");
        Assert.Equal(sosId, queue!.Data!.Items[0].IncidentId);
        Assert.True(queue.Data.Items[0].IsSos);
    }

    [Fact]
    public async Task Replaying_the_same_idempotency_key_does_not_file_a_second_emergency()
    {
        await ResetAsync();
        var first = await ReportAsync(key: "offline-replay-1");

        var again = await Client(Roles.Citizen).PostAsJsonAsync(IncidentsPath, NewReport(key: "offline-replay-1"));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var replay = (await again.Content.ReadFromJsonAsync<ApiEnvelope<IncidentView>>())!.Data!;

        Assert.Equal(first, replay.Id);

        using var scope = _factory.Services.CreateScope();
        var count = await scope.ServiceProvider.GetRequiredService<IncidentsDbContext>()
            .Reports.AsNoTracking().CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Invalid_reports_are_rejected_with_validation_problem_details()
    {
        await ResetAsync();
        var response = await Client(Roles.Citizen).PostAsJsonAsync(IncidentsPath, new
        {
            title = "",
            description = "",
            disasterType = DisasterType.Flood,
            severity = Severity.Severe,
            latitude = 999.0,
            longitude = 90.4125,
            affectedPeopleCount = -4,
            isSos = false,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemView>();
        Assert.NotNull(problem?.Errors);
        Assert.Contains("Title", problem!.Errors!.Keys);
        Assert.Contains("Latitude", problem.Errors.Keys);
    }

    [Fact]
    public async Task Citizens_cannot_read_another_citizens_report_or_reach_responder_surfaces()
    {
        await ResetAsync();
        var incidentId = await ReportAsync();

        // The rescue queue and verification are responder-only.
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(Roles.Citizen).GetAsync($"{RescuePath}/queue")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Client(Roles.Citizen).PostAsJsonAsync($"{IncidentsPath}/{incidentId}/verify", new { approved = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Client(Roles.Rescuer).PostAsJsonAsync($"{IncidentsPath}/{incidentId}/verify", new { approved = true })).StatusCode);

        // Anonymous callers get nothing at all.
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync(IncidentsPath)).StatusCode);

        // A citizen listing incidents only ever sees their own, and a foreign id is a 404.
        var mine = await Client(Roles.Citizen).GetFromJsonAsync<ApiEnvelope<PagedResult<IncidentView>>>(IncidentsPath);
        Assert.All(mine!.Data!.Items, x => Assert.Equal(FakeAuthHandler.SeedUserIds[Roles.Citizen], x.ReporterId));

        Assert.Equal(HttpStatusCode.NotFound,
            (await Client(Roles.Citizen).GetAsync($"{IncidentsPath}/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Illegal_mission_transitions_are_refused()
    {
        await ResetAsync();
        var incidentId = await ReportAsync();

        var assign = await Client(Roles.Rescuer).PostAsJsonAsync($"{RescuePath}/missions",
            new { incidentId, teamId = (Guid?)null, missionTitle = (string?)null, priority = (string?)null });
        var mission = (await assign.Content.ReadFromJsonAsync<ApiEnvelope<MissionView>>())!.Data!;

        // Assigned → Completed skips the field steps.
        var skip = await Client(Roles.Rescuer).PostAsJsonAsync($"{RescuePath}/missions/{mission.Id}/status",
            new { status = MissionStatus.Completed, notes = (string?)null });
        Assert.Equal(HttpStatusCode.Conflict, skip.StatusCode);

        // A second mission for the same incident is refused.
        var duplicate = await Client(Roles.Rescuer).PostAsJsonAsync($"{RescuePath}/missions",
            new { incidentId, teamId = (Guid?)null, missionTitle = (string?)null, priority = (string?)null });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Rejecting_a_report_requires_a_reason_and_closes_the_incident()
    {
        await ResetAsync();
        var incidentId = await ReportAsync();

        var noReason = await Client(Roles.Government).PostAsJsonAsync($"{IncidentsPath}/{incidentId}/verify",
            new { approved = false, reason = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        var rejected = await Client(Roles.Government).PostAsJsonAsync($"{IncidentsPath}/{incidentId}/verify",
            new { approved = false, reason = "Duplicate of an existing report." });
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Equal(IncidentStatus.Rejected, await StatusAsync(incidentId));

        // A closed report can no longer be assigned.
        var assign = await Client(Roles.Rescuer).PostAsJsonAsync($"{RescuePath}/missions",
            new { incidentId, teamId = (Guid?)null, missionTitle = (string?)null, priority = (string?)null });
        Assert.Equal(HttpStatusCode.Conflict, assign.StatusCode);
    }

    private sealed record IncidentView(Guid Id, Guid ReporterId, IncidentStatus Status, double? PriorityScore, bool IsSos);

    private sealed record QueueView(Guid IncidentId, bool IsSos, double? PriorityScore);

    private sealed record MissionView(Guid Id, Guid IncidentId, MissionStatus Status);

    private sealed record ValidationProblemView(Dictionary<string, string[]>? Errors);
}
