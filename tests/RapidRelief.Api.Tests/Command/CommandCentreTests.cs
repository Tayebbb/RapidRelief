using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RapidRelief.Api.Features.Audit.Data;
using RapidRelief.Api.Features.Incidents.Data;
using RapidRelief.Api.Features.Realtime.Data;
using RapidRelief.Api.Features.Relief.Data;
using RapidRelief.Api.Features.Rescue.Data;
using RapidRelief.Api.Features.Rescue.Domain;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Api.Tests.Command;

/// <summary>
/// The government command surfaces: KPIs computed from real rows, incident search and closure,
/// registry management, warehouse coverage, and the audit trail that makes all of it traceable.
/// </summary>
public sealed class CommandCentreTests : IClassFixture<TestingWebAppFactory>
{
    private const string IncidentsPath = "/api/incidents";
    private const string RescuePath = "/api/rescue";
    private const string ReliefPath = "/api/relief";
    private const string AuditPath = "/api/audit";

    private readonly TestingWebAppFactory _factory;

    public CommandCentreTests(TestingWebAppFactory factory) => _factory = factory;

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
        var audit = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        await rescue.MissionLogs.ExecuteDeleteAsync();
        await rescue.Missions.ExecuteDeleteAsync();
        await rescue.TeamMembers.ExecuteDeleteAsync();
        await rescue.Teams.ExecuteDeleteAsync();
        await incidents.StatusHistory.ExecuteDeleteAsync();
        await incidents.Media.ExecuteDeleteAsync();
        await incidents.Reports.ExecuteDeleteAsync();
        await relief.Dispatches.ExecuteDeleteAsync();
        await relief.Requests.ExecuteDeleteAsync();
        await relief.Resources.ExecuteDeleteAsync();
        await audit.Entries.ExecuteDeleteAsync();
        await notifications.Reads.ExecuteDeleteAsync();
        await notifications.Notifications.ExecuteDeleteAsync();
    }

    private async Task<Guid> ReportAsync(
        string title,
        DisasterType type,
        Severity severity,
        bool sos = false,
        string area = "Sector 3",
        string? key = null)
    {
        var response = await Client(Roles.Citizen).PostAsJsonAsync(IncidentsPath, new
        {
            title,
            description = "Detailed enough for the validator to accept the report.",
            disasterType = type,
            severity,
            latitude = 23.8103,
            longitude = 90.4125,
            addressOrArea = area,
            affectedPeopleCount = 3,
            isSos = sos,
            contactPhone = "+8801711234567",
            idempotencyKey = key,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiEnvelope<IncidentView>>())!.Data!.Id;
    }

    [Fact]
    public async Task Dashboard_kpis_are_computed_from_real_rows_not_constants()
    {
        await ResetAsync();

        var sos = await ReportAsync("Collapse", DisasterType.Earthquake, Severity.Catastrophic, sos: true, area: "Block A");
        await ReportAsync("Flooded lane", DisasterType.Flood, Severity.Moderate, area: "Block B");
        var closed = await ReportAsync("Small fire", DisasterType.Fire, Severity.Minor, area: "Block B");

        await Client(Roles.Government).PostAsJsonAsync($"{IncidentsPath}/{closed}/verify", new { approved = true });
        var closeResponse = await Client(Roles.Government)
            .PostAsJsonAsync($"{IncidentsPath}/{closed}/resolve", new { notes = "Handled by the local fire post." });
        Assert.Equal(HttpStatusCode.OK, closeResponse.StatusCode);

        var summary = (await Client(Roles.Government)
            .GetFromJsonAsync<ApiEnvelope<SummaryView>>($"{IncidentsPath}/ops/summary"))!.Data!;

        Assert.Equal(3, summary.Kpi.TotalIncidents);
        Assert.Equal(2, summary.Kpi.ActiveIncidents);
        Assert.Equal(1, summary.Kpi.CriticalIncidents);
        Assert.Equal(1, summary.Kpi.SosOpen);
        Assert.Equal(2, summary.Kpi.Unassigned);
        Assert.Equal(1, summary.Kpi.ResolvedLast24h);
        Assert.Equal(3, summary.Kpi.NewLast24h);
        Assert.Equal(33.3, summary.Kpi.ResolutionRatePercent, 1);

        // Distributions mirror what was actually filed.
        Assert.Equal(1, summary.ByType.Single(t => t.Key == nameof(DisasterType.Earthquake)).Count);
        Assert.Equal(1, summary.BySeverity.Single(s => s.Key == nameof(Severity.Catastrophic)).Count);
        Assert.Equal(2, summary.ByStatus.Single(s => s.Key == nameof(IncidentStatus.Reported)).Count);

        // The SOS block is the one that should read as escalating.
        var hotspot = summary.Hotspots.Single(h => h.Area == "Block A");
        Assert.Equal(1, hotspot.Critical);
        Assert.Equal("Escalating", hotspot.Trend);
        Assert.Contains(summary.Hotspots, h => h.Area == "Block B");

        Assert.Contains(summary.Daily, d => d.Reported == 3);
        Assert.Equal(sos, sos);
    }

    [Fact]
    public async Task Average_response_time_measures_report_to_dispatch()
    {
        await ResetAsync();
        var incidentId = await ReportAsync("Trapped resident", DisasterType.Flood, Severity.Severe);
        await Client(Roles.Government).PostAsJsonAsync($"{IncidentsPath}/{incidentId}/verify", new { approved = true });

        var beforeDispatch = (await Client(Roles.Government)
            .GetFromJsonAsync<ApiEnvelope<SummaryView>>($"{IncidentsPath}/ops/summary"))!.Data!;
        Assert.Null(beforeDispatch.Kpi.AvgResponseMinutes);

        var teamId = await CreateTeamAsync("Response clock");
        var assign = await Client(Roles.Government).PostAsJsonAsync($"{RescuePath}/missions", new { incidentId, teamId });
        Assert.Equal(HttpStatusCode.Created, assign.StatusCode);

        var afterDispatch = (await Client(Roles.Government)
            .GetFromJsonAsync<ApiEnvelope<SummaryView>>($"{IncidentsPath}/ops/summary"))!.Data!;
        Assert.NotNull(afterDispatch.Kpi.AvgResponseMinutes);
        Assert.True(afterDispatch.Kpi.AvgResponseMinutes >= 0);
        Assert.Equal(0, afterDispatch.Kpi.Unassigned);
    }

    [Fact]
    public async Task Incident_search_filters_by_text_severity_type_and_sos()
    {
        await ResetAsync();
        await ReportAsync("Riverbank breach at Mirpur", DisasterType.Flood, Severity.Severe, area: "Mirpur");
        await ReportAsync("Gas leak in Uttara", DisasterType.Fire, Severity.Minor, sos: true, area: "Uttara");

        var government = Client(Roles.Government);

        var byText = (await government.GetFromJsonAsync<ApiEnvelope<PagedResult<IncidentView>>>(
            $"{IncidentsPath}?q=mirpur"))!.Data!;
        Assert.Single(byText.Items);
        Assert.Contains("Mirpur", byText.Items[0].Title);

        var bySeverity = (await government.GetFromJsonAsync<ApiEnvelope<PagedResult<IncidentView>>>(
            $"{IncidentsPath}?severity={Severity.Severe}"))!.Data!;
        Assert.Single(bySeverity.Items);

        var byType = (await government.GetFromJsonAsync<ApiEnvelope<PagedResult<IncidentView>>>(
            $"{IncidentsPath}?type={DisasterType.Fire}"))!.Data!;
        Assert.Single(byType.Items);

        var bySos = (await government.GetFromJsonAsync<ApiEnvelope<PagedResult<IncidentView>>>(
            $"{IncidentsPath}?sos=true"))!.Data!;
        Assert.Single(bySos.Items);
        Assert.True(bySos.Items[0].IsSos);

        var noMatch = (await government.GetFromJsonAsync<ApiEnvelope<PagedResult<IncidentView>>>(
            $"{IncidentsPath}?q=chittagong"))!.Data!;
        Assert.Empty(noMatch.Items);
    }

    [Fact]
    public async Task Closing_an_incident_notifies_the_reporter_and_is_refused_while_a_mission_runs()
    {
        await ResetAsync();
        var incidentId = await ReportAsync("Blocked road", DisasterType.Landslide, Severity.Moderate);
        await Client(Roles.Government).PostAsJsonAsync($"{IncidentsPath}/{incidentId}/verify", new { approved = true });

        var teamId = await CreateTeamAsync("Close guard");
        await Client(Roles.Government).PostAsJsonAsync($"{RescuePath}/missions", new { incidentId, teamId });

        var refused = await Client(Roles.Government)
            .PostAsJsonAsync($"{IncidentsPath}/{incidentId}/resolve", new { notes = "Cleared by the roads authority." });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);

        // A second incident with no mission closes cleanly and reaches the reporter.
        var standalone = await ReportAsync("False alarm", DisasterType.Other, Severity.Minimal, key: "close-2");
        var closed = await Client(Roles.Government)
            .PostAsJsonAsync($"{IncidentsPath}/{standalone}/resolve", new { notes = "Caller confirmed everyone is safe." });
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        Assert.Equal(IncidentStatus.Resolved,
            (await closed.Content.ReadFromJsonAsync<ApiEnvelope<IncidentView>>())!.Data!.Status);

        var blank = await Client(Roles.Government)
            .PostAsJsonAsync($"{IncidentsPath}/{standalone}/resolve", new { notes = "" });
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var citizenId = FakeAuthHandler.SeedUserIds[Roles.Citizen];
        var pushed = await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>()
            .Notifications.AsNoTracking()
            .Where(x => x.UserId == citizenId)
            .ToListAsync();
        Assert.Contains(pushed, x => x.Summary.Contains("closed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Administrative_actions_are_written_to_the_audit_trail_with_who_what_and_result()
    {
        await ResetAsync();
        var incidentId = await ReportAsync("Auditable call", DisasterType.Flood, Severity.Severe);

        await Client(Roles.Government).PostAsJsonAsync($"{IncidentsPath}/{incidentId}/verify", new { approved = true });
        var teamId = await CreateTeamAsync("Audited crew");
        await Client(Roles.Government).PostAsJsonAsync($"{RescuePath}/missions", new { incidentId, teamId });

        var trail = (await Client(Roles.Government)
            .GetFromJsonAsync<ApiEnvelope<PagedResult<AuditEntryDto>>>(AuditPath))!.Data!;

        var verified = trail.Items.Single(x => x.Action == "Incident.Verify");
        Assert.Equal("Incident", verified.EntityType);
        Assert.Equal(incidentId.ToString(), verified.EntityId);
        Assert.Equal("Verified", verified.Result);
        Assert.Equal(FakeAuthHandler.SeedUserIds[Roles.Government], verified.ActorId);
        Assert.True(verified.OccurredAtUtc > DateTimeOffset.UtcNow.AddMinutes(-5));

        Assert.Contains(trail.Items, x => x.Action == "Team.Create" && x.EntityId == teamId.ToString());
        Assert.Contains(trail.Items, x => x.Action == "Mission.Assign" && x.Result == "Assigned");

        // Filters narrow the trail the same way the UI does.
        var onlyTeams = (await Client(Roles.Government)
            .GetFromJsonAsync<ApiEnvelope<PagedResult<AuditEntryDto>>>($"{AuditPath}?entityType=RescueTeam"))!.Data!;
        Assert.All(onlyTeams.Items, x => Assert.Equal("RescueTeam", x.EntityType));

        var facets = (await Client(Roles.Government)
            .GetFromJsonAsync<ApiEnvelope<FacetsView>>($"{AuditPath}/actions"))!.Data!;
        Assert.Contains("Incident.Verify", facets.Actions);
        Assert.Contains("Mission", facets.EntityTypes);
    }

    [Fact]
    public async Task The_audit_trail_is_government_only()
    {
        await ResetAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await Client(Roles.Rescuer).GetAsync(AuditPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(Roles.Citizen).GetAsync(AuditPath)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Client(Roles.Rescuer).GetAsync($"{ReliefPath}/resources")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync(AuditPath)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await Client(Roles.Government).GetAsync(AuditPath)).StatusCode);
    }

    [Fact]
    public async Task Team_registry_edits_are_validated_guarded_and_audited()
    {
        await ResetAsync();
        var teamId = await CreateTeamAsync("Registry crew", "General");

        var renamed = await Client(Roles.Government).PutAsJsonAsync($"{RescuePath}/teams/{teamId}", new
        {
            teamName = "Mirpur Water Rescue",
            specialization = "WaterRescue",
            contactNumber = "+8801799112233",
            status = TeamStatus.Available,
        });
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var team = (await renamed.Content.ReadFromJsonAsync<ApiEnvelope<TeamView>>())!.Data!;
        Assert.Equal("Mirpur Water Rescue", team.TeamName);
        Assert.Equal("WaterRescue", team.Specialization);

        var unknownStatus = await Client(Roles.Government).PutAsJsonAsync($"{RescuePath}/teams/{teamId}", new
        {
            teamName = "Mirpur Water Rescue",
            status = "Sleeping",
        });
        Assert.Equal(HttpStatusCode.BadRequest, unknownStatus.StatusCode);

        // A deployed team cannot be stood down from the registry either.
        var incidentId = await ReportAsync("Registry guard", DisasterType.Flood, Severity.Severe, key: "registry");
        await Client(Roles.Government).PostAsJsonAsync($"{RescuePath}/missions", new { incidentId, teamId });

        var midMission = await Client(Roles.Government).PutAsJsonAsync($"{RescuePath}/teams/{teamId}", new
        {
            teamName = "Mirpur Water Rescue",
            status = TeamStatus.OffDuty,
        });
        Assert.Equal(HttpStatusCode.Conflict, midMission.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await Client(Roles.Rescuer).PutAsJsonAsync(
            $"{RescuePath}/teams/{teamId}", new { teamName = "Hijacked" })).StatusCode);

        var trail = (await Client(Roles.Government)
            .GetFromJsonAsync<ApiEnvelope<PagedResult<AuditEntryDto>>>($"{AuditPath}?action=Team.Update"))!.Data!;
        Assert.Contains(trail.Items, x => x.Summary.Contains("Mirpur Water Rescue", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inventory_reports_open_demand_and_flags_supply_types_with_no_stock()
    {
        await ResetAsync();

        var citizen = Client(Roles.Citizen);
        Assert.Equal(HttpStatusCode.Created, (await citizen.PostAsJsonAsync($"{ReliefPath}/requests", new
        {
            type = ResourceType.Water,
            quantity = 40,
            recipientCount = 8,
            urgency = "High",
            latitude = 23.81,
            longitude = 90.41,
            deliveryAddress = "Camp 2",
            idempotencyKey = "inv-water",
        })).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await citizen.PostAsJsonAsync($"{ReliefPath}/requests", new
        {
            type = ResourceType.Medicine,
            quantity = 12,
            recipientCount = 12,
            urgency = "Critical",
            latitude = 23.81,
            longitude = 90.41,
            deliveryAddress = "Camp 2",
            idempotencyKey = "inv-med",
        })).StatusCode);

        var government = Client(Roles.Government);
        var created = await government.PostAsJsonAsync($"{ReliefPath}/resources", new
        {
            name = "Drinking water 5 L",
            category = ResourceType.Water,
            totalQuantity = 100d,
            allocatedQuantity = 90d,
            unit = "Cans",
            warehouseLocation = "Tejgaon depot",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var inventory = (await government.GetFromJsonAsync<ApiEnvelope<InventoryView>>($"{ReliefPath}/resources"))!.Data!;
        var water = inventory.Items.Single();
        Assert.Equal(10, water.AvailableQuantity);
        Assert.Equal(40, water.OpenDemand);

        // Medicine was requested but nothing is stocked — that gap must be visible, not silent.
        Assert.Contains(inventory.UncoveredDemand, g => g.Category == ResourceType.Medicine && g.OpenDemand == 12);

        var overCommitted = await government.PutAsJsonAsync($"{ReliefPath}/resources/{water.Id}", new
        {
            name = water.Name,
            category = ResourceType.Water,
            totalQuantity = 50d,
            allocatedQuantity = 80d,
            unit = "Cans",
        });
        Assert.Equal(HttpStatusCode.BadRequest, overCommitted.StatusCode);
    }

    [Fact]
    public async Task Command_decisions_actually_move_the_citizen_and_rescue_workflows()
    {
        await ResetAsync();
        var incidentId = await ReportAsync("End to end", DisasterType.Flood, Severity.Severe, sos: true, key: "e2e");

        // The queue never stalls on the command centre, so the report is visible before review —
        // but as an unverified call, and verification is what flips it to dispatchable.
        var beforeQueue = (await Client(Roles.Rescuer)
            .GetFromJsonAsync<ApiEnvelope<QueueView>>($"{RescuePath}/queue"))!.Data!;
        Assert.Equal(IncidentStatus.Reported, beforeQueue.Items.Single(x => x.IncidentId == incidentId).Status);

        Assert.Equal(HttpStatusCode.OK, (await Client(Roles.Government)
            .PostAsJsonAsync($"{IncidentsPath}/{incidentId}/verify", new { approved = true })).StatusCode);

        var afterQueue = (await Client(Roles.Rescuer)
            .GetFromJsonAsync<ApiEnvelope<QueueView>>($"{RescuePath}/queue"))!.Data!;
        Assert.Equal(IncidentStatus.Verified, afterQueue.Items.Single(x => x.IncidentId == incidentId).Status);

        // Registering a team in the command centre makes it dispatchable immediately.
        var teamId = await CreateTeamAsync("Command crew", "WaterRescue");
        var suitable = (await Client(Roles.Government)
            .GetFromJsonAsync<ApiEnvelope<List<SuitabilityView>>>($"{RescuePath}/teams/suitable?incidentId={incidentId}"))!.Data!;
        Assert.Contains(suitable, x => x.TeamId == teamId);

        // Locking an account is reflected in the user list the command centre reads.
        var users = (await Client(Roles.Government)
            .GetFromJsonAsync<ApiEnvelope<PagedResult<UserSummaryDto>>>("/api/auth/users?pageSize=100"))!.Data!;
        var target = users.Items.First(u => u.Id != FakeAuthHandler.SeedUserIds[Roles.Government] && !u.IsLocked);
        Assert.Equal(HttpStatusCode.NoContent, (await Client(Roles.Government)
            .PostAsJsonAsync($"/api/auth/users/{target.Id}/lock", new { locked = true })).StatusCode);

        var afterLock = (await Client(Roles.Government)
            .GetFromJsonAsync<ApiEnvelope<PagedResult<UserSummaryDto>>>("/api/auth/users?pageSize=100"))!.Data!;
        Assert.True(afterLock.Items.Single(u => u.Id == target.Id).IsLocked);

        var trail = (await Client(Roles.Government)
            .GetFromJsonAsync<ApiEnvelope<PagedResult<AuditEntryDto>>>($"{AuditPath}?entityType=User"))!.Data!;
        Assert.Contains(trail.Items, x => x.Action == "User.Lock" && x.EntityId == target.Id.ToString());

        // Restore the account so later tests see the seeded state.
        await Client(Roles.Government).PostAsJsonAsync($"/api/auth/users/{target.Id}/lock", new { locked = false });
    }

    private async Task<Guid> CreateTeamAsync(string name, string specialization = "General")
    {
        var response = await Client(Roles.Government).PostAsJsonAsync($"{RescuePath}/teams", new
        {
            teamName = name,
            specialization,
            contactNumber = "+8801700000000",
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiEnvelope<TeamView>>())!.Data!.Id;
    }

    private sealed record IncidentView(Guid Id, string Title, IncidentStatus Status, bool IsSos);

    private sealed record TeamView(Guid Id, string TeamName, string Specialization, string Status);

    private sealed record KpiView(
        int ActiveIncidents,
        int CriticalIncidents,
        int SosOpen,
        int Unassigned,
        int AwaitingTeam,
        int InProgress,
        int ResolvedLast24h,
        int NewLast24h,
        double? AvgResponseMinutes,
        double? AvgResolutionMinutes,
        double ResolutionRatePercent,
        int TotalIncidents);

    private sealed record CountView(string Key, int Count);

    private sealed record BucketView(string Day, int Reported, int Resolved);

    private sealed record HotspotView(string Area, int Total, int Critical, int Last6h, int Previous6h, string Trend);

    private sealed record SummaryView(
        KpiView Kpi,
        IReadOnlyList<CountView> ByStatus,
        IReadOnlyList<CountView> ByType,
        IReadOnlyList<CountView> BySeverity,
        IReadOnlyList<BucketView> Daily,
        IReadOnlyList<HotspotView> Hotspots);

    private sealed record FacetsView(IReadOnlyList<string> Actions, IReadOnlyList<string> EntityTypes);

    private sealed record ResourceView(
        Guid Id,
        string Name,
        ResourceType Category,
        double TotalQuantity,
        double AllocatedQuantity,
        double AvailableQuantity,
        double OpenDemand);

    private sealed record GapView(ResourceType Category, double OpenDemand);

    private sealed record InventoryView(IReadOnlyList<ResourceView> Items, IReadOnlyList<GapView> UncoveredDemand);

    private sealed record QueueItemView(Guid IncidentId, IncidentStatus Status, string Band);

    private sealed record QueueView(IReadOnlyList<QueueItemView> Items);

    private sealed record SuitabilityView(Guid TeamId, string TeamName, string Status);
}
