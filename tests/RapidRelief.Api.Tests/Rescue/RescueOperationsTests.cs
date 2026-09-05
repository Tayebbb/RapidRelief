using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RapidRelief.Api.Features.Incidents.Data;
using RapidRelief.Api.Features.Realtime.Data;
using RapidRelief.Api.Features.Rescue.Data;
using RapidRelief.Api.Features.Rescue.Domain;
using RapidRelief.Api.Features.Rescue.Endpoints;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Tests.Rescue;

/// <summary>
/// Priority incident → assignment → navigation data → live mission → resolution, including the
/// guards that stop two teams being sent to the same emergency.
/// </summary>
public sealed class RescueOperationsTests : IClassFixture<TestingWebAppFactory>
{
    private const string IncidentsPath = "/api/incidents";
    private const string RescuePath = "/api/rescue";

    private readonly TestingWebAppFactory _factory;

    public RescueOperationsTests(TestingWebAppFactory factory) => _factory = factory;

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

    private async Task<Guid> ReportAsync(bool sos, Severity severity, string? key = null)
    {
        var response = await Client(Roles.Citizen).PostAsJsonAsync(IncidentsPath, new
        {
            title = sos ? "SOS" : "Flooded street",
            description = "Water rising quickly around the block; people on the roof.",
            disasterType = DisasterType.Flood,
            severity,
            latitude = 23.8103,
            longitude = 90.4125,
            addressOrArea = "Sector 3",
            affectedPeopleCount = 4,
            isSos = sos,
            contactPhone = "+8801711234567",
            idempotencyKey = key,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiEnvelope<IncidentView>>())!.Data!.Id;
    }

    private async Task<Guid> CreateTeamAsync(string name, string specialization = "General", Guid? leadId = null)
    {
        var response = await Client(Roles.Government).PostAsJsonAsync($"{RescuePath}/teams", new
        {
            teamName = name,
            specialization,
            contactNumber = "+8801700000000",
            teamLeadUserId = leadId,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiEnvelope<TeamView>>())!.Data!.Id;
    }

    [Fact]
    public async Task Dashboard_bands_incidents_and_reports_mission_counts()
    {
        await ResetAsync();
        await ReportAsync(sos: true, Severity.Severe, "band-sos");
        await ReportAsync(sos: false, Severity.Severe, "band-high");
        await ReportAsync(sos: false, Severity.Moderate, "band-medium");
        await ReportAsync(sos: false, Severity.Minor, "band-low");

        var dashboard = (await Client(Roles.Rescuer)
            .GetFromJsonAsync<ApiEnvelope<DashboardView>>($"{RescuePath}/dashboard?lat=23.81&lng=90.41"))!.Data!;

        Assert.Equal(1, dashboard.QueueByBand["Critical"]);
        Assert.Equal(1, dashboard.QueueByBand["High"]);
        Assert.Equal(1, dashboard.QueueByBand["Medium"]);
        Assert.Equal(1, dashboard.QueueByBand["Low"]);

        // Critical is surfaced separately and nearby calls carry a real distance for navigation.
        Assert.Single(dashboard.Critical);
        Assert.True(dashboard.Critical[0].IsSos);
        Assert.All(dashboard.Nearby, x => Assert.NotNull(x.DistanceKm));
    }

    [Fact]
    public async Task Team_suitability_prefers_a_free_nearby_team_and_hides_off_duty_units()
    {
        await ResetAsync();
        var incidentId = await ReportAsync(sos: false, Severity.Severe, "suitability");

        var closeBusy = await CreateTeamAsync("Close but busy", "WaterRescue");
        var farFree = await CreateTeamAsync("Further but free", "WaterRescue");
        var offDuty = await CreateTeamAsync("Resting crew");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RescueDbContext>();
            var teams = await db.Teams.ToListAsync();
            teams.Single(t => t.Id == closeBusy).CurrentLatitude = 23.8105;
            teams.Single(t => t.Id == closeBusy).CurrentLongitude = 90.4126;
            teams.Single(t => t.Id == farFree).CurrentLatitude = 23.8400;
            teams.Single(t => t.Id == farFree).CurrentLongitude = 90.4400;
            teams.Single(t => t.Id == offDuty).Status = TeamStatus.OffDuty;
            await db.SaveChangesAsync();
        }

        // The close team is already running something else.
        var other = await ReportAsync(sos: false, Severity.Minor, "suitability-other");
        await Client(Roles.Government).PostAsJsonAsync($"{RescuePath}/missions", new { incidentId = other, teamId = closeBusy });

        var ranked = (await Client(Roles.Government)
            .GetFromJsonAsync<ApiEnvelope<List<SuitabilityView>>>($"{RescuePath}/teams/suitable?incidentId={incidentId}"))!.Data!;

        Assert.Equal(farFree, ranked[0].TeamId);
        Assert.DoesNotContain(ranked, x => x.TeamId == offDuty);
        Assert.Contains(ranked[0].Reasons, r => r.Contains("free now", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Assignment_prevents_conflicts_on_both_the_incident_and_the_team()
    {
        await ResetAsync();
        var first = await ReportAsync(sos: false, Severity.Severe, "conflict-1");
        var second = await ReportAsync(sos: false, Severity.Severe, "conflict-2");
        var teamId = await CreateTeamAsync("Alpha");

        var assign = await Client(Roles.Government).PostAsJsonAsync($"{RescuePath}/missions", new { incidentId = first, teamId });
        Assert.Equal(HttpStatusCode.Created, assign.StatusCode);

        // Same incident, another team → refused.
        var otherTeam = await CreateTeamAsync("Bravo");
        var duplicate = await Client(Roles.Government).PostAsJsonAsync($"{RescuePath}/missions", new { incidentId = first, teamId = otherTeam });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        // Same team, another incident → refused, a unit can only be in one place.
        var doubleBooked = await Client(Roles.Government).PostAsJsonAsync($"{RescuePath}/missions", new { incidentId = second, teamId });
        Assert.Equal(HttpStatusCode.Conflict, doubleBooked.StatusCode);

        // Off-duty units cannot be dispatched at all.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RescueDbContext>();
            (await db.Teams.SingleAsync(t => t.Id == otherTeam)).Status = TeamStatus.OffDuty;
            await db.SaveChangesAsync();
        }

        var offDuty = await Client(Roles.Government).PostAsJsonAsync($"{RescuePath}/missions", new { incidentId = second, teamId = otherTeam });
        Assert.Equal(HttpStatusCode.Conflict, offDuty.StatusCode);
    }

    [Fact]
    public async Task Rejecting_a_dispatch_returns_the_incident_to_the_queue_and_frees_the_team()
    {
        await ResetAsync();
        var incidentId = await ReportAsync(sos: false, Severity.Severe, "reject-flow");
        await Client(Roles.Government).PostAsJsonAsync($"{IncidentsPath}/{incidentId}/verify", new { approved = true });

        var assign = await Client(Roles.Rescuer).PostAsJsonAsync($"{RescuePath}/missions", new { incidentId });
        var mission = (await assign.Content.ReadFromJsonAsync<ApiEnvelope<MissionView>>())!.Data!;

        var noReason = await Client(Roles.Rescuer).PostAsJsonAsync($"{RescuePath}/missions/{mission.Id}/reject", new { reason = "" });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        var reject = await Client(Roles.Rescuer).PostAsJsonAsync($"{RescuePath}/missions/{mission.Id}/reject",
            new { reason = "Boat engine failure" });
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);

        // The incident is assignable again and the team is back to Available.
        using var scope = _factory.Services.CreateScope();
        var incident = await scope.ServiceProvider.GetRequiredService<IncidentsDbContext>()
            .Reports.AsNoTracking().SingleAsync(x => x.Id == incidentId);
        Assert.Equal(IncidentStatus.Verified, incident.Status);
        Assert.Null(incident.AssignedMissionId);

        var team = await scope.ServiceProvider.GetRequiredService<RescueDbContext>().Teams.AsNoTracking().SingleAsync();
        Assert.Equal(TeamStatus.Available, team.Status);

        var retry = await Client(Roles.Government).PostAsJsonAsync($"{RescuePath}/missions", new { incidentId, teamId = team.Id });
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
    }

    [Fact]
    public async Task Government_can_reassign_a_live_mission_to_another_team()
    {
        await ResetAsync();
        var incidentId = await ReportAsync(sos: false, Severity.Severe, "reassign-flow");
        var alpha = await CreateTeamAsync("Alpha");
        var bravo = await CreateTeamAsync("Bravo");

        var assign = await Client(Roles.Government).PostAsJsonAsync($"{RescuePath}/missions", new { incidentId, teamId = alpha });
        var mission = (await assign.Content.ReadFromJsonAsync<ApiEnvelope<MissionView>>())!.Data!;

        // A rescuer may not reassign — that is a dispatcher decision.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Client(Roles.Rescuer).PostAsJsonAsync($"{RescuePath}/missions/{mission.Id}/reassign", new { teamId = bravo })).StatusCode);

        var reassign = await Client(Roles.Government).PostAsJsonAsync($"{RescuePath}/missions/{mission.Id}/reassign",
            new { teamId = bravo, reason = "Alpha is closer to a second call" });
        Assert.Equal(HttpStatusCode.OK, reassign.StatusCode);
        var replacement = (await reassign.Content.ReadFromJsonAsync<ApiEnvelope<MissionView>>())!.Data!;
        Assert.Equal(bravo, replacement.AssignedTeamId);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RescueDbContext>();
        Assert.Equal(MissionStatus.Cancelled, (await db.Missions.AsNoTracking().SingleAsync(x => x.Id == mission.Id)).Status);
        Assert.Equal(TeamStatus.Available, (await db.Teams.AsNoTracking().SingleAsync(x => x.Id == alpha)).Status);
        Assert.Equal(TeamStatus.Dispatched, (await db.Teams.AsNoTracking().SingleAsync(x => x.Id == bravo)).Status);

        // The incident follows the new mission, not the cancelled one.
        var incident = await scope.ServiceProvider.GetRequiredService<IncidentsDbContext>()
            .Reports.AsNoTracking().SingleAsync(x => x.Id == incidentId);
        Assert.Equal(bravo, incident.AssignedTeamId);
        Assert.Equal(IncidentStatus.Assigned, incident.Status);
    }

    [Fact]
    public async Task Mission_states_record_timestamps_and_refuse_invalid_transitions()
    {
        await ResetAsync();
        var incidentId = await ReportAsync(sos: true, Severity.Catastrophic, "states");

        var assign = await Client(Roles.Rescuer).PostAsJsonAsync($"{RescuePath}/missions", new { incidentId });
        var mission = (await assign.Content.ReadFromJsonAsync<ApiEnvelope<MissionView>>())!.Data!;

        Assert.Equal(HttpStatusCode.Conflict,
            (await Client(Roles.Rescuer).PostAsJsonAsync($"{RescuePath}/missions/{mission.Id}/status",
                new { status = MissionStatus.OnScene })).StatusCode);

        await Client(Roles.Rescuer).PostAsJsonAsync($"{RescuePath}/missions/{mission.Id}/accept", new { });
        await Client(Roles.Rescuer).PostAsJsonAsync($"{RescuePath}/missions/{mission.Id}/status", new { status = MissionStatus.EnRoute });
        await Client(Roles.Rescuer).PostAsJsonAsync($"{RescuePath}/missions/{mission.Id}/status", new { status = MissionStatus.OnScene });
        var complete = await Client(Roles.Rescuer).PostAsJsonAsync($"{RescuePath}/missions/{mission.Id}/status",
            new { status = MissionStatus.Completed, notes = "Four people extracted" });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        var final = (await complete.Content.ReadFromJsonAsync<ApiEnvelope<MissionView>>())!.Data!;
        Assert.NotNull(final.AcceptedAtUtc);
        Assert.NotNull(final.StartedAtUtc);
        Assert.NotNull(final.OnSceneAtUtc);
        Assert.NotNull(final.CompletedAtUtc);
        Assert.True(final.AcceptedAtUtc <= final.StartedAtUtc);
        Assert.True(final.StartedAtUtc <= final.OnSceneAtUtc);
        Assert.True(final.OnSceneAtUtc <= final.CompletedAtUtc);

        // A closed mission is closed.
        Assert.Equal(HttpStatusCode.Conflict,
            (await Client(Roles.Rescuer).PostAsJsonAsync($"{RescuePath}/missions/{mission.Id}/status",
                new { status = MissionStatus.EnRoute })).StatusCode);

        using var scope = _factory.Services.CreateScope();
        var incident = await scope.ServiceProvider.GetRequiredService<IncidentsDbContext>()
            .Reports.AsNoTracking().SingleAsync(x => x.Id == incidentId);
        Assert.Equal(IncidentStatus.Resolved, incident.Status);
        Assert.Equal(TeamStatus.Available,
            (await scope.ServiceProvider.GetRequiredService<RescueDbContext>().Teams.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Team_status_is_consistent_with_active_work()
    {
        await ResetAsync();
        var incidentId = await ReportAsync(sos: false, Severity.Severe, "team-status");

        var assign = await Client(Roles.Rescuer).PostAsJsonAsync($"{RescuePath}/missions", new { incidentId });
        var mission = (await assign.Content.ReadFromJsonAsync<ApiEnvelope<MissionView>>())!.Data!;

        // Going off duty mid-mission is refused.
        var offDuty = await Client(Roles.Rescuer).PostAsJsonAsync($"{RescuePath}/teams/mine/status", new { status = TeamStatus.OffDuty });
        Assert.Equal(HttpStatusCode.Conflict, offDuty.StatusCode);

        var invalid = await Client(Roles.Rescuer).PostAsJsonAsync($"{RescuePath}/teams/mine/status", new { status = "Napping" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        await Client(Roles.Rescuer).PostAsJsonAsync($"{RescuePath}/missions/{mission.Id}/status", new { status = MissionStatus.EnRoute });
        await Client(Roles.Rescuer).PostAsJsonAsync($"{RescuePath}/missions/{mission.Id}/status", new { status = MissionStatus.OnScene });
        await Client(Roles.Rescuer).PostAsJsonAsync($"{RescuePath}/missions/{mission.Id}/status", new { status = MissionStatus.Completed });

        var afterwards = await Client(Roles.Rescuer).PostAsJsonAsync($"{RescuePath}/teams/mine/status", new { status = TeamStatus.OffDuty });
        Assert.Equal(HttpStatusCode.OK, afterwards.StatusCode);
        Assert.Equal(TeamStatus.OffDuty, (await afterwards.Content.ReadFromJsonAsync<ApiEnvelope<TeamView>>())!.Data!.Status);
    }

    [Fact]
    public async Task Responders_see_the_callback_number_and_the_assigned_team_gets_notified()
    {
        await ResetAsync();
        var incidentId = await ReportAsync(sos: true, Severity.Catastrophic, "detail");

        var forResponder = (await Client(Roles.Rescuer)
            .GetFromJsonAsync<ApiEnvelope<IncidentView>>($"{IncidentsPath}/{incidentId}"))!.Data!;
        Assert.Equal("+8801711234567", forResponder.ContactPhone);

        var teamId = await CreateTeamAsync("Notify crew", "WaterRescue", FakeAuthHandler.SeedUserIds[Roles.Rescuer]);
        await Client(Roles.Government).PostAsJsonAsync($"{RescuePath}/missions", new { incidentId, teamId });

        using var scope = _factory.Services.CreateScope();
        var rescuerId = FakeAuthHandler.SeedUserIds[Roles.Rescuer];
        var pushed = await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>()
            .Notifications.AsNoTracking()
            .Where(x => x.UserId == rescuerId && x.Topic == RescueEndpoints.MissionTopic)
            .ToListAsync();

        Assert.NotEmpty(pushed);
        Assert.Contains(pushed, x => x.Summary.Contains("New mission", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record IncidentView(Guid Id, IncidentStatus Status, string? ContactPhone);

    private sealed record MissionView(
        Guid Id,
        Guid IncidentId,
        Guid AssignedTeamId,
        MissionStatus Status,
        DateTimeOffset? AcceptedAtUtc,
        DateTimeOffset? StartedAtUtc,
        DateTimeOffset? OnSceneAtUtc,
        DateTimeOffset? CompletedAtUtc);

    private sealed record TeamView(Guid Id, string TeamName, string Status);

    private sealed record SuitabilityView(Guid TeamId, string TeamName, double? DistanceKm, int ActiveMissions, IReadOnlyList<string> Reasons);

    private sealed record QueueView(Guid IncidentId, bool IsSos, string Band, double? DistanceKm);

    private sealed record DashboardView(
        Dictionary<string, int> QueueByBand,
        List<QueueView> Critical,
        List<QueueView> Nearby,
        int AssignedMissions,
        int ActiveMissions,
        int CompletedMissions);
}
