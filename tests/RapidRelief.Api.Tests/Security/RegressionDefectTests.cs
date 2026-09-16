using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RapidRelief.Api.Features.Alerts.Data;
using RapidRelief.Api.Features.Alerts.Endpoints;
using RapidRelief.Api.Features.Incidents.Data;
using RapidRelief.Api.Features.Incidents.Endpoints;
using RapidRelief.Api.Features.Relief.Data;
using RapidRelief.Api.Features.Relief.Endpoints;
using RapidRelief.Api.Features.Rescue.Data;
using RapidRelief.Api.Features.Rescue.Domain;
using RapidRelief.Api.Features.Rescue.Endpoints;
using RapidRelief.Api.Features.Shelters.Data;
using RapidRelief.Api.Features.Shelters.Domain;
using RapidRelief.Api.Features.Shelters.Endpoints;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Tests.Security;

/// <summary>
/// Dedicated regression test suite ensuring all discovered defects remain permanently resolved.
/// </summary>
public sealed class RegressionDefectTests : IClassFixture<TestingWebAppFactory>
{
    private readonly TestingWebAppFactory _factory;

    public RegressionDefectTests(TestingWebAppFactory factory) => _factory = factory;

    private HttpClient Client(string? role = null)
    {
        var client = _factory.CreateClient();
        if (role is not null)
        {
            client.DefaultRequestHeaders.Add(FakeAuthHandler.HeaderName, role);
        }
        return client;
    }

    // =========================================================================
    // BUG 1 REGRESSION: Rescue Position Status Bypass during Active Mission
    // =========================================================================
    [Fact]
    public async Task Bug1_Regression_Rescuer_cannot_change_status_away_from_Dispatched_via_position_update_while_on_active_mission()
    {
        using var scope = _factory.Services.CreateScope();
        var rescueDb = scope.ServiceProvider.GetRequiredService<RescueDbContext>();
        var incidentsDb = scope.ServiceProvider.GetRequiredService<IncidentsDbContext>();

        await rescueDb.MissionLogs.ExecuteDeleteAsync();
        await rescueDb.Missions.ExecuteDeleteAsync();
        await rescueDb.TeamMembers.ExecuteDeleteAsync();
        await rescueDb.Teams.ExecuteDeleteAsync();
        await incidentsDb.StatusHistory.ExecuteDeleteAsync();
        await incidentsDb.Reports.ExecuteDeleteAsync();

        var rescuerUserId = FakeAuthHandler.SeedUserIds[Roles.Rescuer];

        // 1. Create a team with this rescuer as lead
        var team = new RescueTeam
        {
            Id = Guid.NewGuid(),
            TeamName = "Alpha Rescue Unit",
            Specialization = "WaterRescue",
            ContactNumber = "+8801700000000",
            TeamLeadUserId = rescuerUserId,
            Status = TeamStatus.Dispatched,
            CurrentLatitude = 23.8103,
            CurrentLongitude = 90.4125,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        rescueDb.Teams.Add(team);

        var member = new RescueTeamMember
        {
            Id = Guid.NewGuid(),
            TeamId = team.Id,
            RescuerUserId = rescuerUserId,
            JoinedAtUtc = DateTimeOffset.UtcNow,
        };
        rescueDb.TeamMembers.Add(member);

        // 2. Create an incident and active mission assigned to this team
        var incidentId = Guid.NewGuid();
        var mission = new RescueMission
        {
            Id = Guid.NewGuid(),
            IncidentId = incidentId,
            AssignedTeamId = team.Id,
            MissionTitle = "Rescue trapped citizens",
            AssignedByUserId = Guid.NewGuid(),
            Status = MissionStatus.EnRoute,
            AssignedAtUtc = DateTimeOffset.UtcNow,
        };
        rescueDb.Missions.Add(mission);
        await rescueDb.SaveChangesAsync();

        var rescuerClient = Client(Roles.Rescuer);

        // 3. Attempting to update position with Status = "Available" while mission is active must return 409 Conflict
        var conflictResp1 = await rescuerClient.PostAsJsonAsync("/api/rescue/teams/mine/position", new
        {
            latitude = 23.8200,
            longitude = 90.4200,
            status = TeamStatus.Available
        });
        Assert.Equal(HttpStatusCode.Conflict, conflictResp1.StatusCode);

        // 4. Attempting to update position with Status = "OffDuty" while mission is active must return 409 Conflict
        var conflictResp2 = await rescuerClient.PostAsJsonAsync("/api/rescue/teams/mine/position", new
        {
            latitude = 23.8200,
            longitude = 90.4200,
            status = TeamStatus.OffDuty
        });
        Assert.Equal(HttpStatusCode.Conflict, conflictResp2.StatusCode);

        // 5. Updating position with Status = "Dispatched" or null status should succeed
        var okResp = await rescuerClient.PostAsJsonAsync("/api/rescue/teams/mine/position", new
        {
            latitude = 23.8200,
            longitude = 90.4200,
            status = TeamStatus.Dispatched
        });
        Assert.Equal(HttpStatusCode.NoContent, okResp.StatusCode);
    }

    // =========================================================================
    // BUG 2 REGRESSION: Relief Resource Allocation Exceeding Stock
    // =========================================================================
    [Fact]
    public async Task Bug2_Regression_Creating_or_updating_resource_with_Allocated_greater_than_Total_is_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var reliefDb = scope.ServiceProvider.GetRequiredService<ReliefDbContext>();
        await reliefDb.Resources.ExecuteDeleteAsync();

        var govClient = Client(Roles.Government);

        // 1. Attempt to create resource with AllocatedQuantity (100) > TotalQuantity (10) -> 400 Bad Request
        var invalidCreateResp = await govClient.PostAsJsonAsync("/api/relief/resources", new ReliefResourceRequest(
            Name: "Test Supplies",
            Category: ResourceType.Food,
            TotalQuantity: 10,
            AllocatedQuantity: 100,
            Unit: "Boxes",
            WarehouseLocation: "Warehouse 1"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidCreateResp.StatusCode);

        // 2. Create a valid resource
        var validCreateResp = await govClient.PostAsJsonAsync("/api/relief/resources", new ReliefResourceRequest(
            Name: "Test Supplies",
            Category: ResourceType.Food,
            TotalQuantity: 100,
            AllocatedQuantity: 10,
            Unit: "Boxes",
            WarehouseLocation: "Warehouse 1"));
        Assert.Equal(HttpStatusCode.Created, validCreateResp.StatusCode);
        var created = (await validCreateResp.Content.ReadFromJsonAsync<ApiEnvelope<ReliefResourceDto>>())!.Data!;

        // 3. Attempt to update resource with AllocatedQuantity (150) > TotalQuantity (100) -> 400 Bad Request
        var invalidUpdateResp = await govClient.PutAsJsonAsync($"/api/relief/resources/{created.Id}", new ReliefResourceRequest(
            Name: "Test Supplies Updated",
            Category: ResourceType.Food,
            TotalQuantity: 100,
            AllocatedQuantity: 150,
            Unit: "Boxes",
            WarehouseLocation: "Warehouse 1"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidUpdateResp.StatusCode);
    }

    // =========================================================================
    // BUG 3 REGRESSION: Missing Enum Validation in Shelters
    // =========================================================================
    [Fact]
    public async Task Bug3_Regression_Shelter_create_and_update_with_invalid_enum_is_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var opsDb = scope.ServiceProvider.GetRequiredService<OpsDbContext>();
        await opsDb.Shelters.ExecuteDeleteAsync();

        var adminClient = Client(Roles.Admin);

        // 1. Attempt Create with invalid Status integer -> 400 Bad Request
        var invalidCreateResp = await adminClient.PostAsJsonAsync("/api/shelters", new
        {
            name = "Invalid Status Shelter",
            latitude = 23.8,
            longitude = 90.4,
            capacity = 100,
            currentOccupancy = 0,
            facilities = new List<string>(),
            status = 999
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidCreateResp.StatusCode);

        // 2. Create valid shelter
        var validCreateResp = await adminClient.PostAsJsonAsync("/api/shelters", new CreateShelterRequest(
            Name: "Valid Shelter",
            Latitude: 23.8,
            Longitude: 90.4,
            Capacity: 100,
            CurrentOccupancy: 0,
            Facilities: new(),
            Status: ShelterStatus.Open));
        Assert.Equal(HttpStatusCode.Created, validCreateResp.StatusCode);
        var shelter = (await validCreateResp.Content.ReadFromJsonAsync<ApiEnvelope<ShelterDto>>())!.Data!;

        // 3. Attempt Update with invalid Status integer -> 400 Bad Request
        var invalidUpdateResp = await adminClient.PutAsJsonAsync($"/api/shelters/{shelter.Id}", new
        {
            name = "Valid Shelter",
            latitude = 23.8,
            longitude = 90.4,
            capacity = 100,
            currentOccupancy = 0,
            facilities = new List<string>(),
            status = 999
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidUpdateResp.StatusCode);
    }

    // =========================================================================
    // BUG 4 REGRESSION: Missing Enum Validation in Alerts
    // =========================================================================
    [Fact]
    public async Task Bug4_Regression_Alert_create_with_invalid_enum_is_rejected()
    {
        using var scope = _factory.Services.CreateScope();
        var alertsDb = scope.ServiceProvider.GetRequiredService<AlertsDbContext>();
        await alertsDb.Alerts.ExecuteDeleteAsync();

        var govClient = Client(Roles.Government);

        // 1. Invalid Severity integer -> 400 Bad Request
        var invalidSeverityResp = await govClient.PostAsJsonAsync("/api/alerts", new
        {
            title = "Warning",
            body = "Severe storm approaching",
            severity = 999,
            disasterType = (int)DisasterType.Flood,
            targetArea = "Dhaka",
            expiresAtUtc = DateTimeOffset.UtcNow.AddHours(2)
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidSeverityResp.StatusCode);

        // 2. Invalid DisasterType integer -> 400 Bad Request
        var invalidDisasterResp = await govClient.PostAsJsonAsync("/api/alerts", new
        {
            title = "Warning",
            body = "Severe storm approaching",
            severity = (int)Severity.Severe,
            disasterType = 999,
            targetArea = "Dhaka",
            expiresAtUtc = DateTimeOffset.UtcNow.AddHours(2)
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidDisasterResp.StatusCode);
    }

    // =========================================================================
    // BUG 5 REGRESSION: Missing Element Validation on Incident Photo Paths
    // =========================================================================
    [Fact]
    public async Task Bug5_Regression_Incident_create_with_overlong_photo_path_or_empty_path_is_rejected()
    {
        var citizenClient = Client(Roles.Citizen);

        // 1. Photo path exceeding 500 characters -> 400 Bad Request
        var overlongPath = "https://cdn.rapidrelief.org/media/" + new string('x', 500);
        var invalidLengthResp = await citizenClient.PostAsJsonAsync("/api/incidents", new
        {
            title = "Photo path test",
            description = "Test incident description",
            disasterType = DisasterType.Flood,
            severity = Severity.Moderate,
            latitude = 23.8,
            longitude = 90.4,
            addressOrArea = "Area 1",
            affectedPeopleCount = 2,
            isSos = false,
            photoPaths = new[] { overlongPath }
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidLengthResp.StatusCode);

        // 2. Empty photo path string -> 400 Bad Request
        var emptyPathResp = await citizenClient.PostAsJsonAsync("/api/incidents", new
        {
            title = "Photo path test 2",
            description = "Test incident description",
            disasterType = DisasterType.Flood,
            severity = Severity.Moderate,
            latitude = 23.8,
            longitude = 90.4,
            addressOrArea = "Area 1",
            affectedPeopleCount = 2,
            isSos = false,
            photoPaths = new[] { "" }
        });
        Assert.Equal(HttpStatusCode.BadRequest, emptyPathResp.StatusCode);
    }
}
