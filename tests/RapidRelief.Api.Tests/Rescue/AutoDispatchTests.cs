using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RapidRelief.Api.Features.Incidents.Data;
using RapidRelief.Api.Features.Rescue.Data;
using RapidRelief.Api.Features.Rescue.Domain;
using RapidRelief.Api.Features.Rescue.Handlers;
using RapidRelief.Api.Features.Rescue.Services;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Events;

namespace RapidRelief.Api.Tests.Rescue;

public sealed class AutoDispatchTests : IClassFixture<TestingWebAppFactory>
{
    private const string IncidentsPath = "/api/incidents";
    private readonly TestingWebAppFactory _factory;

    public AutoDispatchTests(TestingWebAppFactory factory) => _factory = factory;

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

        await rescue.MissionLogs.ExecuteDeleteAsync();
        await rescue.Missions.ExecuteDeleteAsync();
        await rescue.TeamMembers.ExecuteDeleteAsync();
        await rescue.Teams.ExecuteDeleteAsync();
        await incidents.StatusHistory.ExecuteDeleteAsync();
        await incidents.Media.ExecuteDeleteAsync();
        await incidents.Reports.ExecuteDeleteAsync();
    }

    private async Task<Guid> SeedTeamAsync(string name, string spec, double lat, double lng, string status = TeamStatus.Available)
    {
        using var scope = _factory.Services.CreateScope();
        var rescue = scope.ServiceProvider.GetRequiredService<RescueDbContext>();

        var team = new RescueTeam
        {
            Id = Guid.NewGuid(),
            TeamName = name,
            Specialization = spec,
            Status = status,
            CurrentLatitude = lat,
            CurrentLongitude = lng,
            TeamLeadUserId = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        rescue.Teams.Add(team);
        await rescue.SaveChangesAsync();
        return team.Id;
    }

    private async Task<Guid> SeedIncidentAsync(bool isSos, Severity severity, DisasterType type = DisasterType.Flood)
    {
        using var scope = _factory.Services.CreateScope();
        var incidents = scope.ServiceProvider.GetRequiredService<IncidentsDbContext>();

        var incident = new RapidRelief.Api.Features.Incidents.Domain.IncidentReport
        {
            Id = Guid.NewGuid(),
            ReporterId = Guid.NewGuid(),
            Title = isSos ? "SOS Emergency" : "Disaster report",
            Description = "Trapped by rising water; need immediate rescue team.",
            DisasterType = type,
            Severity = severity,
            Status = IncidentStatus.Verified,
            Latitude = 23.8103,
            Longitude = 90.4125,
            AddressOrArea = "Mirpur",
            AffectedPeopleCount = 5,
            IsSos = isSos,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };

        incidents.Reports.Add(incident);
        await incidents.SaveChangesAsync();
        return incident.Id;
    }

    [Fact]
    public async Task Critical_incident_automatically_dispatches_best_available_team()
    {
        await ResetAsync();
        var teamId = await SeedTeamAsync("Mirpur USAR Unit", "Water, Flood", 23.8120, 90.4150);
        var incidentId = await SeedIncidentAsync(isSos: false, severity: Severity.Catastrophic);

        using var scope = _factory.Services.CreateScope();
        var autoDispatch = scope.ServiceProvider.GetRequiredService<IAutoDispatchService>();
        var rescueDb = scope.ServiceProvider.GetRequiredService<RescueDbContext>();

        var outcome = await autoDispatch.EvaluateAndDispatchAsync(incidentId, priorityScore: 82.0, severity: Severity.Catastrophic);

        Assert.True(outcome.Dispatched);
        Assert.Equal(teamId, outcome.TeamId);
        Assert.NotNull(outcome.MissionId);

        // Verify database persistence
        var mission = await rescueDb.Missions.FirstOrDefaultAsync(m => m.Id == outcome.MissionId);
        Assert.NotNull(mission);
        Assert.Equal(incidentId, mission.IncidentId);
        Assert.Equal(teamId, mission.AssignedTeamId);
        Assert.Equal(MissionStatus.Assigned, mission.Status);
        Assert.Equal(AutoDispatchService.AutoDispatchActorId, mission.AssignedByUserId);

        var team = await rescueDb.Teams.FirstOrDefaultAsync(t => t.Id == teamId);
        Assert.NotNull(team);
        Assert.Equal(TeamStatus.Dispatched, team.Status);
    }

    [Fact]
    public async Task SOS_incident_triggers_auto_dispatch_even_with_moderate_score()
    {
        await ResetAsync();
        var teamId = await SeedTeamAsync("Fire Rescue Unit", "Fire, General", 23.8100, 90.4120);
        var incidentId = await SeedIncidentAsync(isSos: true, severity: Severity.Moderate, type: DisasterType.Fire);

        using var scope = _factory.Services.CreateScope();
        var autoDispatch = scope.ServiceProvider.GetRequiredService<IAutoDispatchService>();

        var outcome = await autoDispatch.EvaluateAndDispatchAsync(incidentId, priorityScore: 60.0, severity: Severity.Moderate);

        Assert.True(outcome.Dispatched);
        Assert.Equal(teamId, outcome.TeamId);
    }

    [Fact]
    public async Task Routine_low_priority_incident_is_not_auto_dispatched()
    {
        await ResetAsync();
        await SeedTeamAsync("Standby Unit", "Flood", 23.8120, 90.4150);
        var incidentId = await SeedIncidentAsync(isSos: false, severity: Severity.Minor);

        using var scope = _factory.Services.CreateScope();
        var autoDispatch = scope.ServiceProvider.GetRequiredService<IAutoDispatchService>();
        var rescueDb = scope.ServiceProvider.GetRequiredService<RescueDbContext>();

        var outcome = await autoDispatch.EvaluateAndDispatchAsync(incidentId, priorityScore: 25.0, severity: Severity.Minor);

        Assert.False(outcome.Dispatched);
        Assert.Null(outcome.MissionId);

        var missions = await rescueDb.Missions.Where(m => m.IncidentId == incidentId).ToListAsync();
        Assert.Empty(missions);
    }

    [Fact]
    public async Task When_no_teams_are_available_incident_remains_unassigned_for_manual_action()
    {
        await ResetAsync();
        // Seed team that is OffDuty
        await SeedTeamAsync("OffDuty Unit", "Flood", 23.8120, 90.4150, status: TeamStatus.OffDuty);
        var incidentId = await SeedIncidentAsync(isSos: false, severity: Severity.Catastrophic);

        using var scope = _factory.Services.CreateScope();
        var autoDispatch = scope.ServiceProvider.GetRequiredService<IAutoDispatchService>();
        var rescueDb = scope.ServiceProvider.GetRequiredService<RescueDbContext>();

        var outcome = await autoDispatch.EvaluateAndDispatchAsync(incidentId, priorityScore: 90.0, severity: Severity.Catastrophic);

        Assert.False(outcome.Dispatched);
        var missions = await rescueDb.Missions.Where(m => m.IncidentId == incidentId).ToListAsync();
        Assert.Empty(missions);
    }

    [Fact]
    public async Task AutoDispatchIncidentAssessedHandler_executes_on_event()
    {
        await ResetAsync();
        var teamId = await SeedTeamAsync("Event Test Unit", "Flood", 23.8120, 90.4150);
        var incidentId = await SeedIncidentAsync(isSos: false, severity: Severity.Catastrophic);

        using var scope = _factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<AutoDispatchIncidentAssessedHandler>();
        var rescueDb = scope.ServiceProvider.GetRequiredService<RescueDbContext>();

        var evt = new IncidentAssessed(incidentId, Severity.Catastrophic, 85.0, "Catastrophic flooding", null);
        await handler.HandleAsync(evt);

        var mission = await rescueDb.Missions.FirstOrDefaultAsync(m => m.IncidentId == incidentId);
        Assert.NotNull(mission);
        Assert.Equal(teamId, mission.AssignedTeamId);
    }
}
