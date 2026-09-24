using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RapidRelief.Api.Features.Ai.Data;
using RapidRelief.Api.Features.Ai.Endpoints;
using RapidRelief.Api.Features.Alerts.Data;
using RapidRelief.Api.Features.Alerts.Endpoints;
using RapidRelief.Api.Features.Auth.Data;
using RapidRelief.Api.Features.Auth.Endpoints;
using RapidRelief.Api.Features.Incidents.Data;
using RapidRelief.Api.Features.Incidents.Endpoints;
using RapidRelief.Api.Features.Realtime.Data;
using RapidRelief.Api.Features.Realtime.Endpoints;
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
using RapidRelief.Shared.Contracts.ReadModels;
using Xunit;
using AlertSeverity = RapidRelief.Shared.Contracts.Enums.Severity;

namespace RapidRelief.Api.Tests.SystemWorkflows;

/// <summary>
/// Phase 5 (System Testing) & Phase 19 (Acceptance Testing) Full Test Suite.
/// Tests complete cross-cutting business workflows and end-to-end user persona journeys
/// across all 9 architectural slices of RapidRelief.
/// </summary>
public sealed class FullSystemAndAcceptanceTests : IClassFixture<TestingWebAppFactory>
{
    private readonly TestingWebAppFactory _factory;

    public FullSystemAndAcceptanceTests(TestingWebAppFactory factory)
    {
        _factory = factory;
    }

    private HttpClient Client(string? role = null)
    {
        var client = _factory.CreateClient();
        if (role is not null)
        {
            client.DefaultRequestHeaders.Add(FakeAuthHandler.HeaderName, role);
        }
        return client;
    }

    private async Task ResetStateAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var incidents = scope.ServiceProvider.GetRequiredService<IncidentsDbContext>();
        var rescue = scope.ServiceProvider.GetRequiredService<RescueDbContext>();
        var relief = scope.ServiceProvider.GetRequiredService<ReliefDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var alerts = scope.ServiceProvider.GetRequiredService<AlertsDbContext>();
        var ai = scope.ServiceProvider.GetRequiredService<AiDbContext>();
        var shelters = scope.ServiceProvider.GetRequiredService<OpsDbContext>();

        await rescue.MissionLogs.ExecuteDeleteAsync();
        await rescue.Missions.ExecuteDeleteAsync();
        await rescue.TeamMembers.ExecuteDeleteAsync();
        await rescue.Teams.ExecuteDeleteAsync();
        await incidents.StatusHistory.ExecuteDeleteAsync();
        await incidents.Media.ExecuteDeleteAsync();
        await incidents.Reports.ExecuteDeleteAsync();
        await relief.Requests.ExecuteDeleteAsync();
        await relief.Resources.ExecuteDeleteAsync();
        await notifications.Reads.ExecuteDeleteAsync();
        await notifications.Notifications.ExecuteDeleteAsync();
        await alerts.Alerts.ExecuteDeleteAsync();
        await ai.AssistantMessages.ExecuteDeleteAsync();
        await shelters.Shelters.ExecuteDeleteAsync();
    }

    // =========================================================================
    // LEVEL 3: FULL SYSTEM TESTING WORKFLOWS
    // =========================================================================

    [Fact]
    public async Task SystemWorkflow1_Emergency_SOS_to_rescue_dispatch_and_resolution_pipeline()
    {
        await ResetStateAsync();

        var citizen = Client(Roles.Citizen);
        var rescuer = Client(Roles.Rescuer);
        var gov = Client(Roles.Government);

        // 1. Citizen reports SOS incident
        var createReportReq = new CreateIncidentRequest(
            Title: "Severe Flood Trapping 4 Family Members",
            Description: "Water rising rapidly up to the first floor ceiling. Immediate boat rescue needed.",
            DisasterType: DisasterType.Flood,
            Severity: AlertSeverity.Catastrophic,
            Latitude: 23.8103,
            Longitude: 90.4125,
            AddressOrArea: "Section 6, Mirpur, Dhaka",
            AffectedPeopleCount: 4,
            IsSos: true,
            ContactPhone: "+8801711000000",
            PhotoPaths: null,
            IdempotencyKey: Guid.NewGuid().ToString("N")
        );

        var reportRes = await citizen.PostAsJsonAsync("/api/incidents", createReportReq);
        Assert.Equal(HttpStatusCode.Created, reportRes.StatusCode);
        var incidentEnvelope = await reportRes.Content.ReadFromJsonAsync<ApiEnvelope<IncidentDto>>();
        Assert.NotNull(incidentEnvelope);
        var incidentId = incidentEnvelope.Data.Id;
        Assert.Equal(IncidentStatus.Reported, incidentEnvelope.Data.Status);
        Assert.True(incidentEnvelope.Data.IsSos);

        // 2. Government reviews and verifies the incident
        var verifyRes = await gov.PostAsJsonAsync($"/api/incidents/{incidentId}/verify", new VerifyIncidentRequest(
            Approved: true,
            Reason: "Verified via satellite and local authority confirm."
        ));
        Assert.Equal(HttpStatusCode.OK, verifyRes.StatusCode);
        var verifiedEnvelope = await verifyRes.Content.ReadFromJsonAsync<ApiEnvelope<IncidentDto>>();
        Assert.Equal(IncidentStatus.Verified, verifiedEnvelope!.Data.Status);

        // 3. Government creates a Rescue Team
        var teamRes = await gov.PostAsJsonAsync("/api/rescue/teams", new CreateTeamRequest(
            TeamName: "Alpha Rapid Boat Unit",
            Specialization: "WaterRescue",
            ContactNumber: "+8801700000000",
            TeamLeadUserId: FakeAuthHandler.SeedUserIds[Roles.Rescuer]
        ));
        Assert.Equal(HttpStatusCode.Created, teamRes.StatusCode);
        var teamEnvelope = await teamRes.Content.ReadFromJsonAsync<ApiEnvelope<RescueTeamDto>>();
        Assert.NotNull(teamEnvelope);
        var teamId = teamEnvelope.Data.Id;

        // 4. Government dispatches the rescue team
        var dispatchReq = new AssignMissionRequest(
            IncidentId: incidentId,
            TeamId: teamId,
            MissionTitle: "High priority flood rescue dispatch.",
            Priority: "Critical"
        );
        var dispatchRes = await gov.PostAsJsonAsync("/api/rescue/missions", dispatchReq);
        Assert.Equal(HttpStatusCode.Created, dispatchRes.StatusCode);
        var missionEnvelope = await dispatchRes.Content.ReadFromJsonAsync<ApiEnvelope<RescueMissionDto>>();
        Assert.NotNull(missionEnvelope);
        var missionId = missionEnvelope.Data.Id;

        // 5. Rescuer updates beacon coordinates while en route
        var beaconReq = new TeamPositionRequest(23.8115, 90.4120, TeamStatus.Dispatched);
        var beaconRes = await rescuer.PostAsJsonAsync("/api/rescue/teams/mine/position", beaconReq);
        Assert.Equal(HttpStatusCode.NoContent, beaconRes.StatusCode);

        // 6. Rescuer transitions mission: EnRoute -> OnScene -> Completed
        var enRouteRes = await rescuer.PostAsJsonAsync($"/api/rescue/missions/{missionId}/status", new UpdateMissionStatusRequest(MissionStatus.EnRoute, "Departing base towards location."));
        Assert.Equal(HttpStatusCode.OK, enRouteRes.StatusCode);

        var onSceneRes = await rescuer.PostAsJsonAsync($"/api/rescue/missions/{missionId}/status", new UpdateMissionStatusRequest(MissionStatus.OnScene, "Arrived at flood location, initiating boat rescue."));
        Assert.Equal(HttpStatusCode.OK, onSceneRes.StatusCode);

        var completeRes = await rescuer.PostAsJsonAsync($"/api/rescue/missions/{missionId}/status", new UpdateMissionStatusRequest(MissionStatus.Completed, "All 4 victims safely evacuated to Mirpur shelter."));
        Assert.Equal(HttpStatusCode.OK, completeRes.StatusCode);

        // 7. Verify the Incident was automatically updated to Resolved via Mission completion event projection
        var getIncidentRes = await gov.GetAsync($"/api/incidents/{incidentId}");
        Assert.Equal(HttpStatusCode.OK, getIncidentRes.StatusCode);
        var resolvedEnvelope = await getIncidentRes.Content.ReadFromJsonAsync<ApiEnvelope<IncidentDto>>();
        Assert.NotNull(resolvedEnvelope);
        Assert.Equal(IncidentStatus.Resolved, resolvedEnvelope.Data.Status);
        Assert.Equal("Completed", resolvedEnvelope.Data.MissionStage);

        // Attempting to resolve an already-closed incident returns 409 Conflict
        var dupResolveRes = await gov.PostAsJsonAsync($"/api/incidents/{incidentId}/resolve", new ResolveIncidentRequest("Area cleared."));
        Assert.Equal(HttpStatusCode.Conflict, dupResolveRes.StatusCode);

        // 8. Test command-centre direct resolution on a standalone incident (non-mission flow)
        var standaloneReportReq = new CreateIncidentRequest(
            Title: "Minor water logging near market",
            Description: "Water accumulated but receded on its own.",
            DisasterType: DisasterType.Flood,
            Severity: AlertSeverity.Minor,
            Latitude: 23.75,
            Longitude: 90.38,
            AddressOrArea: "Dhanmondi Market",
            AffectedPeopleCount: 1,
            IsSos: false,
            ContactPhone: "+8801700000000",
            PhotoPaths: null,
            IdempotencyKey: Guid.NewGuid().ToString("N")
        );
        var standaloneRes = await citizen.PostAsJsonAsync("/api/incidents", standaloneReportReq);
        Assert.Equal(HttpStatusCode.Created, standaloneRes.StatusCode);
        var standaloneEnv = await standaloneRes.Content.ReadFromJsonAsync<ApiEnvelope<IncidentDto>>();
        var standaloneId = standaloneEnv!.Data.Id;

        var verifyStandalone = await gov.PostAsJsonAsync($"/api/incidents/{standaloneId}/verify", new VerifyIncidentRequest(true, null));
        Assert.Equal(HttpStatusCode.OK, verifyStandalone.StatusCode);

        var directResolveRes = await gov.PostAsJsonAsync($"/api/incidents/{standaloneId}/resolve", new ResolveIncidentRequest(
            Notes: "Resolved without mission dispatch — water receded naturally."
        ));
        Assert.Equal(HttpStatusCode.OK, directResolveRes.StatusCode);
        var directResolvedEnv = await directResolveRes.Content.ReadFromJsonAsync<ApiEnvelope<IncidentDto>>();
        Assert.Equal(IncidentStatus.Resolved, directResolvedEnv!.Data.Status);

        // 9. Verify Citizen Notifications are recorded
        var notifsRes = await citizen.GetAsync("/api/realtime/notifications");
        Assert.Equal(HttpStatusCode.OK, notifsRes.StatusCode);
        var notifsEnvelope = await notifsRes.Content.ReadFromJsonAsync<ApiEnvelope<NotificationPage>>();
        Assert.NotNull(notifsEnvelope);
        Assert.NotEmpty(notifsEnvelope.Data.Items);
    }

    [Fact]
    public async Task SystemWorkflow2_Relief_supply_chain_lifecycle_from_request_to_delivery()
    {
        await ResetStateAsync();

        var citizen = Client(Roles.Citizen);
        var gov = Client(Roles.Government);

        // 1. Government sets up initial warehouse inventory
        var addWaterRes = await gov.PostAsJsonAsync("/api/relief/resources", new
        {
            Name = "Emergency Mineral Water 5L Packs",
            Type = ResourceType.Water,
            TotalQuantity = 500,
            AllocatedQuantity = 0,
            Unit = "bottles",
            WarehouseLocation = "Central Dhaka Relief Hub",
            Latitude = 23.7500,
            Longitude = 90.3900
        });
        Assert.Equal(HttpStatusCode.Created, addWaterRes.StatusCode);
        var waterResource = await addWaterRes.Content.ReadFromJsonAsync<ApiEnvelope<ReliefResourceDto>>();
        Assert.NotNull(waterResource);
        var resourceId = waterResource.Data.Id;

        // 2. Citizen requests relief supplies
        var reliefReq = new CreateReliefRequest(
            Type: ResourceType.Water,
            Quantity: 20,
            RecipientCount: 5,
            Urgency: "High",
            Latitude: 23.7550,
            Longitude: 90.3950,
            DeliveryAddress: "House 12, Road 4, Dhanmondi",
            Notes: "Need clean drinking water urgently.",
            IncidentId: null,
            IdempotencyKey: null
        );
        var reqRes = await citizen.PostAsJsonAsync("/api/relief/requests", reliefReq);
        Assert.Equal(HttpStatusCode.Created, reqRes.StatusCode);
        var reqEnvelope = await reqRes.Content.ReadFromJsonAsync<ApiEnvelope<ReliefRequestDto>>();
        Assert.NotNull(reqEnvelope);
        var requestId = reqEnvelope.Data.Id;
        Assert.Equal(ReliefStatus.Pending, reqEnvelope.Data.Status);

        // 3. Government reviews and approves the request
        var approveRes = await gov.PostAsJsonAsync($"/api/relief/requests/{requestId}/status", new UpdateReliefStatusRequest(
            Status: ReliefStatus.Approved,
            Note: "Request approved for allocation."
        ));
        Assert.Equal(HttpStatusCode.OK, approveRes.StatusCode);

        // 4. Government allocates warehouse resources
        var allocateRes = await gov.PostAsJsonAsync($"/api/relief/requests/{requestId}/status", new UpdateReliefStatusRequest(
            Status: ReliefStatus.Allocated,
            Note: "20 bottles allocated from Central Dhaka Hub."
        ));
        Assert.Equal(HttpStatusCode.OK, allocateRes.StatusCode);

        // Update allocated stock in warehouse
        await gov.PutAsJsonAsync($"/api/relief/resources/{resourceId}", new
        {
            Name = "Emergency Mineral Water 5L Packs",
            Type = ResourceType.Water,
            TotalQuantity = 500,
            AllocatedQuantity = 20,
            Unit = "bottles",
            WarehouseLocation = "Central Dhaka Relief Hub",
            Latitude = 23.7500,
            Longitude = 90.3900
        });

        // 5. Government dispatches the relief supplies
        var dispatchRes = await gov.PostAsJsonAsync($"/api/relief/requests/{requestId}/dispatch", new DispatchRequest(
            ResourceId: resourceId,
            DispatchedQuantity: 20,
            CarrierOrPartner: "Red Crescent Team Alpha"
        ));
        Assert.Equal(HttpStatusCode.Created, dispatchRes.StatusCode);
        var dispatchDto = (await dispatchRes.Content.ReadFromJsonAsync<ApiEnvelope<ReliefDispatchDto>>())!.Data!;

        // 6. Delivery confirmed
        var deliverRes = await gov.PostAsync($"/api/relief/dispatches/{dispatchDto.Id}/deliver", null);
        Assert.Equal(HttpStatusCode.OK, deliverRes.StatusCode);

        // 7. Citizen checks request status in /mine
        var myRequestsRes = await citizen.GetAsync("/api/relief/requests/mine");
        Assert.Equal(HttpStatusCode.OK, myRequestsRes.StatusCode);
        var myRequests = await myRequestsRes.Content.ReadFromJsonAsync<ApiEnvelope<PagedResult<ReliefRequestDto>>>();
        Assert.NotNull(myRequests);
        Assert.Contains(myRequests.Data.Items, r => r.Id == requestId && r.Status == ReliefStatus.Delivered);
    }

    [Fact]
    public async Task SystemWorkflow3_Emergency_alert_broadcast_and_shelter_dynamic_capacity_tracking()
    {
        await ResetStateAsync();

        var admin = Client(Roles.Government);
        var citizen = Client(Roles.Citizen);
        var anonymous = Client();

        // 1. Admin creates a cyclone shelter
        var createShelterReq = new CreateShelterRequest(
            Name: "Mirpur Model School Cyclone Shelter",
            Latitude: 23.8050,
            Longitude: 90.3650,
            Capacity: 200,
            CurrentOccupancy: 50,
            Facilities: new List<string> { "Water", "Medical", "Generator", "Food" },
            Status: ShelterStatus.Open
        );
        var shelterRes = await admin.PostAsJsonAsync("/api/shelters", createShelterReq);
        Assert.Equal(HttpStatusCode.Created, shelterRes.StatusCode);
        var shelterEnvelope = await shelterRes.Content.ReadFromJsonAsync<ApiEnvelope<ShelterDto>>();
        Assert.NotNull(shelterEnvelope);
        var shelterId = shelterEnvelope.Data.Id;

        // 2. Government broadcasts an emergency Cyclone warning alert
        var alertReq = new CreateAlertRequest(
            Title: "Cyclone Warning: Category 4 Landfall Expected",
            Body: "Severe cyclone approaching. All residents in coastal and low lying areas evacuate to nearest shelters immediately.",
            Severity: AlertSeverity.Catastrophic,
            DisasterType: DisasterType.Cyclone,
            TargetArea: "Dhaka and Coastal Regions",
            RadiusKm: 150.0,
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddHours(24)
        );
        var alertRes = await admin.PostAsJsonAsync("/api/alerts", alertReq);
        Assert.Equal(HttpStatusCode.Created, alertRes.StatusCode);

        // 3. Public / Anonymous user fetches active emergency alerts
        var activeAlertsRes = await anonymous.GetAsync("/api/alerts/active");
        Assert.Equal(HttpStatusCode.OK, activeAlertsRes.StatusCode);
        var activeAlerts = await activeAlertsRes.Content.ReadFromJsonAsync<ApiEnvelope<IReadOnlyList<AlertDto>>>();
        Assert.NotNull(activeAlerts);
        Assert.Contains(activeAlerts.Data, a => a.Title.Contains("Cyclone Warning"));

        // 4. Citizen searches for shelters
        var listSheltersRes = await citizen.GetAsync("/api/shelters?page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, listSheltersRes.StatusCode);
        var sheltersList = await listSheltersRes.Content.ReadFromJsonAsync<ApiEnvelope<PagedResult<ShelterDto>>>();
        Assert.NotNull(sheltersList);
        Assert.Contains(sheltersList.Data.Items, s => s.Id == shelterId && s.Status == ShelterStatus.Open);

        // 5. Shelter fills to capacity (200 occupants)
        var updateOccRes = await admin.PatchAsJsonAsync($"/api/shelters/{shelterId}/occupancy", new UpdateOccupancyRequest(200));
        Assert.Equal(HttpStatusCode.OK, updateOccRes.StatusCode);

        // 6. Verify shelter status automatically flipped to Full
        var getShelterRes = await anonymous.GetAsync($"/api/shelters/{shelterId}");
        Assert.Equal(HttpStatusCode.OK, getShelterRes.StatusCode);
        var updatedShelter = await getShelterRes.Content.ReadFromJsonAsync<ApiEnvelope<ShelterDto>>();
        Assert.Equal(200, updatedShelter!.Data.CurrentOccupancy);
        Assert.Equal(ShelterStatus.Full, updatedShelter.Data.Status);
    }

    [Fact]
    public async Task SystemWorkflow4_User_administration_security_governance_and_audit()
    {
        await ResetStateAsync();

        var admin = Client("Admin");

        // 1. Admin retrieves user management directory
        var usersRes = await admin.GetAsync("/api/auth/users?page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, usersRes.StatusCode);
        var usersPage = await usersRes.Content.ReadFromJsonAsync<ApiEnvelope<PagedResult<UserSummaryDto>>>();
        Assert.NotNull(usersPage);
        Assert.NotEmpty(usersPage.Data.Items);

        var targetUserId = FakeAuthHandler.SeedUserIds[Roles.Citizen];

        // 2. Admin promotes citizen to Rescuer role
        var setRolesRes = await admin.PutAsJsonAsync($"/api/auth/users/{targetUserId}/roles", new SetRolesRequest(new[] { Roles.Rescuer }));
        Assert.Equal(HttpStatusCode.NoContent, setRolesRes.StatusCode);

        // 3. Admin locks the account
        var lockRes = await admin.PostAsJsonAsync($"/api/auth/users/{targetUserId}/lock", new SetLockRequest(true));
        Assert.Equal(HttpStatusCode.NoContent, lockRes.StatusCode);

        // 4. Admin restores account access
        var unlockRes = await admin.PostAsJsonAsync($"/api/auth/users/{targetUserId}/lock", new SetLockRequest(false));
        Assert.Equal(HttpStatusCode.NoContent, unlockRes.StatusCode);
    }

    // =========================================================================
    // LEVEL 4: ACCEPTANCE TESTING (PERSONA JOURNEYS)
    // =========================================================================

    [Fact]
    public async Task Acceptance_PersonaA_Citizen_EndToEnd_Journey()
    {
        await ResetStateAsync();
        var citizen = Client(Roles.Citizen);

        // 1. View profile
        var profileRes = await citizen.GetAsync("/api/auth/profile");
        Assert.Equal(HttpStatusCode.OK, profileRes.StatusCode);

        // 2. Update contact info
        var updateProfileRes = await citizen.PutAsJsonAsync("/api/auth/profile", new UpdateProfileRequest(
            DisplayName: "Jane Citizen",
            PhoneNumber: "+8801700112233",
            EmergencyContact: "John Doe (+8801700998877)"
        ));
        Assert.Equal(HttpStatusCode.OK, updateProfileRes.StatusCode);

        // 3. Submit Emergency Report
        var reportRes = await citizen.PostAsJsonAsync("/api/incidents", new CreateIncidentRequest(
            Title: "House submerged in floodwater",
            Description: "We are 3 people stranded on roof without food or water.",
            DisasterType: DisasterType.Flood,
            Severity: AlertSeverity.Severe,
            Latitude: 23.7800,
            Longitude: 90.4200,
            AddressOrArea: "Badda, Dhaka",
            AffectedPeopleCount: 3,
            IsSos: true,
            ContactPhone: "+8801700112233",
            PhotoPaths: null,
            IdempotencyKey: null
        ));
        Assert.Equal(HttpStatusCode.Created, reportRes.StatusCode);

        // 4. Request Emergency Relief
        var reliefRes = await citizen.PostAsJsonAsync("/api/relief/requests", new CreateReliefRequest(
            Type: ResourceType.Food,
            Quantity: 10,
            RecipientCount: 3,
            Urgency: "Urgent",
            Latitude: 23.7800,
            Longitude: 90.4200,
            DeliveryAddress: "Roof of House 4, Badda",
            Notes: "Emergency dry food packs.",
            IncidentId: null,
            IdempotencyKey: null
        ));
        Assert.Equal(HttpStatusCode.Created, reliefRes.StatusCode);

        // 5. Ask AI Assistant for safety guidance
        var aiRes = await citizen.PostAsJsonAsync("/api/ai/assistant/messages", new AssistantMessageRequest(
            SessionId: null,
            Message: "What safety precautions should I take while waiting on roof during a flood?",
            Latitude: null,
            Longitude: null
        ));
        Assert.Equal(HttpStatusCode.OK, aiRes.StatusCode);

        // 6. View My Incidents & My Relief Requests
        var myIncidents = await citizen.GetAsync("/api/incidents/mine");
        Assert.Equal(HttpStatusCode.OK, myIncidents.StatusCode);

        var myRelief = await citizen.GetAsync("/api/relief/requests/mine");
        Assert.Equal(HttpStatusCode.OK, myRelief.StatusCode);
    }

    [Fact]
    public async Task Acceptance_PersonaB_Rescuer_EndToEnd_Journey()
    {
        await ResetStateAsync();
        var gov = Client(Roles.Government);
        var rescuer = Client(Roles.Rescuer);

        // Set up rescuer's team
        await gov.PostAsJsonAsync("/api/rescue/teams", new CreateTeamRequest(
            TeamName: "Bravo Rescue Squad",
            Specialization: "Urban",
            ContactNumber: "+8801722334455",
            TeamLeadUserId: FakeAuthHandler.SeedUserIds[Roles.Rescuer]
        ));

        // 1. Rescuer updates beacon location
        var beaconRes = await rescuer.PostAsJsonAsync("/api/rescue/teams/mine/position", new TeamPositionRequest(
            Latitude: 23.8200,
            Longitude: 90.4100,
            Status: TeamStatus.Available
        ));
        Assert.Equal(HttpStatusCode.NoContent, beaconRes.StatusCode);

        // 2. Rescuer checks command centre operational summary
        var opsRes = await rescuer.GetAsync("/api/incidents/ops/summary?days=7");
        Assert.Equal(HttpStatusCode.OK, opsRes.StatusCode);
    }

    [Fact]
    public async Task Acceptance_PersonaC_GovernmentOperator_EndToEnd_Journey()
    {
        await ResetStateAsync();
        var gov = Client(Roles.Government);

        // 1. View Command Centre KPI dashboard
        var opsSummary = await gov.GetAsync("/api/incidents/ops/summary");
        Assert.Equal(HttpStatusCode.OK, opsSummary.StatusCode);

        // 2. Query all incidents
        var incidentsList = await gov.GetAsync("/api/incidents?page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, incidentsList.StatusCode);

        // 3. Compose critical broadcast alert
        var alertRes = await gov.PostAsJsonAsync("/api/alerts", new CreateAlertRequest(
            Title: "Flash Flood Warning",
            Body: "Rising river levels expected in next 2 hours. Move to higher ground.",
            Severity: AlertSeverity.Catastrophic,
            DisasterType: DisasterType.Flood,
            TargetArea: "Northern Dhaka",
            RadiusKm: 50,
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddHours(12)
        ));
        Assert.Equal(HttpStatusCode.Created, alertRes.StatusCode);
    }

    [Fact]
    public async Task Acceptance_PersonaD_AnonymousVisitor_EndToEnd_Journey()
    {
        await ResetStateAsync();
        var anon = Client();

        // 1. Access public health endpoint
        var healthRes = await anon.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, healthRes.StatusCode);

        // 2. View public map config
        var mapConfigRes = await anon.GetAsync("/api/foundation/map-config");
        Assert.Equal(HttpStatusCode.OK, mapConfigRes.StatusCode);

        // 3. View public active alerts
        var alertsRes = await anon.GetAsync("/api/alerts/active");
        Assert.Equal(HttpStatusCode.OK, alertsRes.StatusCode);

        // 4. View public shelter list
        var sheltersRes = await anon.GetAsync("/api/shelters");
        Assert.Equal(HttpStatusCode.OK, sheltersRes.StatusCode);

        // 5. Attempt protected action without authentication -> 401 Unauthorized
        var protectedRes = await anon.GetAsync("/api/auth/profile");
        Assert.Equal(HttpStatusCode.Unauthorized, protectedRes.StatusCode);
    }
}
