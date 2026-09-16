using System.Net;
using System.Net.Http.Json;
using System.Text;
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
using RapidRelief.Api.Features.Relief.Data;
using RapidRelief.Api.Features.Relief.Endpoints;
using RapidRelief.Api.Features.Rescue.Data;
using RapidRelief.Api.Features.Shelters.Data;
using RapidRelief.Api.Features.Shelters.Domain;
using RapidRelief.Api.Features.Shelters.Endpoints;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using Xunit;
using AlertSeverity = RapidRelief.Shared.Contracts.Enums.Severity;

namespace RapidRelief.Api.Tests.Security;

/// <summary>
/// Phase 7 (Boundary Value Testing), Phase 8 (Invalid Input Testing),
/// Phase 9 (Exceptional Testing), Phase 10 & 11 (Auth & Authz Testing).
/// Comprehensive boundary, error, and security matrix across all RapidRelief slices.
/// </summary>
public sealed class ComprehensiveValidationAndBoundaryTests : IClassFixture<TestingWebAppFactory>
{
    private readonly TestingWebAppFactory _factory;

    public ComprehensiveValidationAndBoundaryTests(TestingWebAppFactory factory)
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
    // 1. BOUNDARY VALUE ANALYSIS (BVA) TESTS
    // =========================================================================

    [Theory]
    [InlineData("Ab1!xyz", HttpStatusCode.BadRequest)]   // 7 chars -> Min-1 (Invalid)
    [InlineData("Ab1!wxyz", HttpStatusCode.Created)]      // 8 chars -> Min (Valid)
    [InlineData("Ab1!wxyzz", HttpStatusCode.Created)]     // 9 chars -> Min+1 (Valid)
    public async Task Register_Password_Length_Boundaries(string password, HttpStatusCode expectedStatus)
    {
        var anon = Client();
        var email = $"pwd_{Guid.NewGuid():N}@test.dev";
        var req = new RegisterRequest(email, password, "Test User", "+8801700000000", "Emergency", Roles.Citizen);

        var res = await anon.PostAsJsonAsync("/api/auth/register", req);
        Assert.Equal(expectedStatus, res.StatusCode);
    }

    [Theory]
    [InlineData(0, HttpStatusCode.BadRequest)]    // 0 chars -> Empty (Invalid)
    [InlineData(1, HttpStatusCode.Created)]       // 1 char -> Min (Valid)
    [InlineData(4000, HttpStatusCode.Created)]    // 4000 chars -> Max (Valid)
    [InlineData(4001, HttpStatusCode.BadRequest)] // 4001 chars -> Max+1 (Invalid)
    public async Task Incident_Description_Length_Boundaries(int length, HttpStatusCode expectedStatus)
    {
        var citizen = Client(Roles.Citizen);
        var desc = new string('A', length);
        var req = new CreateIncidentRequest(
            Title: "Flood In Underserved Area",
            Description: desc,
            DisasterType: DisasterType.Flood,
            Severity: AlertSeverity.Moderate,
            Latitude: 23.8103,
            Longitude: 90.4125,
            AddressOrArea: "Dhaka",
            AffectedPeopleCount: 1,
            IsSos: false,
            ContactPhone: null,
            PhotoPaths: null,
            IdempotencyKey: null
        );

        var res = await citizen.PostAsJsonAsync("/api/incidents", req);
        Assert.Equal(expectedStatus, res.StatusCode);
    }

    [Theory]
    [InlineData(-90.0001, 90.0, HttpStatusCode.BadRequest)]  // Lat < -90 (Invalid)
    [InlineData(-90.0, 90.0, HttpStatusCode.Created)]        // Lat = -90 (Valid)
    [InlineData(90.0, 90.0, HttpStatusCode.Created)]         // Lat = 90 (Valid)
    [InlineData(90.0001, 90.0, HttpStatusCode.BadRequest)]   // Lat > 90 (Invalid)
    [InlineData(0.0, -180.0001, HttpStatusCode.BadRequest)]  // Lng < -180 (Invalid)
    [InlineData(0.0, -180.0, HttpStatusCode.Created)]        // Lng = -180 (Valid)
    [InlineData(0.0, 180.0, HttpStatusCode.Created)]         // Lng = 180 (Valid)
    [InlineData(0.0, 180.0001, HttpStatusCode.BadRequest)]   // Lng > 180 (Invalid)
    public async Task Incident_Coordinate_Boundaries(double lat, double lng, HttpStatusCode expectedStatus)
    {
        var citizen = Client(Roles.Citizen);
        var req = new CreateIncidentRequest(
            Title: "Coordinate Boundary Test Incident",
            Description: "Valid 10+ char description for coordinate bounds testing.",
            DisasterType: DisasterType.Earthquake,
            Severity: AlertSeverity.Severe,
            Latitude: lat,
            Longitude: lng,
            AddressOrArea: "Coordinate Boundary Site",
            AffectedPeopleCount: 1,
            IsSos: false,
            ContactPhone: null,
            PhotoPaths: null,
            IdempotencyKey: null
        );

        var res = await citizen.PostAsJsonAsync("/api/incidents", req);
        Assert.Equal(expectedStatus, res.StatusCode);
    }

    [Theory]
    [InlineData(0, HttpStatusCode.BadRequest)]      // 0 Quantity -> Min-1 (Invalid)
    [InlineData(1, HttpStatusCode.Created)]         // 1 Quantity -> Min (Valid)
    [InlineData(1000, HttpStatusCode.Created)]      // 1000 Quantity -> Max (Valid)
    [InlineData(1001, HttpStatusCode.BadRequest)]   // 1001 Quantity -> Max+1 (Invalid)
    public async Task ReliefRequest_Quantity_Boundaries(int quantity, HttpStatusCode expectedStatus)
    {
        var citizen = Client(Roles.Citizen);
        var req = new CreateReliefRequest(
            Type: ResourceType.Food,
            Quantity: quantity,
            RecipientCount: 1,
            Urgency: "Normal",
            Latitude: 23.75,
            Longitude: 90.38,
            DeliveryAddress: null,
            Notes: null,
            IncidentId: null,
            IdempotencyKey: null
        );

        var res = await citizen.PostAsJsonAsync("/api/relief/requests", req);
        Assert.Equal(expectedStatus, res.StatusCode);
    }

    [Theory]
    [InlineData(1, HttpStatusCode.Created)]         // 1 Capacity -> Min (Valid)
    [InlineData(100000, HttpStatusCode.Created)]    // 100k Capacity -> Max (Valid)
    public async Task Shelter_Capacity_Boundaries(int capacity, HttpStatusCode expectedStatus)
    {
        var admin = Client(Roles.Government);
        var req = new CreateShelterRequest(
            Name: $"Boundary Shelter {Guid.NewGuid():N}",
            Latitude: 23.70,
            Longitude: 90.40,
            Capacity: capacity,
            CurrentOccupancy: 0,
            Facilities: new List<string> { "Water" },
            Status: ShelterStatus.Open
        );

        var res = await admin.PostAsJsonAsync("/api/shelters", req);
        Assert.Equal(expectedStatus, res.StatusCode);
    }

    // =========================================================================
    // 2. INVALID INPUT TESTING
    // =========================================================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("plainaddress")]
    [InlineData("@missingusername.com")]
    [InlineData("spaces in@email.com")]
    public async Task Register_Rejects_Invalid_Email_Formats(string invalidEmail)
    {
        var anon = Client();
        var req = new RegisterRequest(invalidEmail, "ValidP@ss123", "Display Name", null, null, Roles.Citizen);
        var res = await anon.PostAsJsonAsync("/api/auth/register", req);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Alert_Rejects_Invalid_Enum_Values()
    {
        var gov = Client(Roles.Government);
        var rawJson = "{\"Title\":\"Invalid Alert\",\"Body\":\"Test\",\"Severity\":999,\"TargetArea\":\"Dhaka\",\"ExpiresAtUtc\":\"2099-01-01T00:00:00Z\"}";
        var content = new StringContent(rawJson, Encoding.UTF8, "application/json");

        var res = await gov.PostAsync("/api/alerts", content);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Shelter_Rejects_Occupancy_Greater_Than_Capacity()
    {
        var admin = Client(Roles.Government);
        var req = new CreateShelterRequest(
            Name: "Overbooked Shelter",
            Latitude: 23.70,
            Longitude: 90.40,
            Capacity: 100,
            CurrentOccupancy: 150, // Invalid: occupancy > capacity
            Facilities: new List<string>(),
            Status: ShelterStatus.Open
        );

        var res = await admin.PostAsJsonAsync("/api/shelters", req);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task NonExistent_Entity_Returns_404_NotFound()
    {
        var citizen = Client(Roles.Citizen);
        var nonExistentId = Guid.NewGuid();

        var res = await citizen.GetAsync($"/api/incidents/{nonExistentId}");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    // =========================================================================
    // 3. EXCEPTIONAL & SECURITY / AUTHORIZATION TESTS
    // =========================================================================

    [Fact]
    public async Task Citizen_Cannot_Access_Admin_User_Management()
    {
        var citizen = Client(Roles.Citizen);
        var res = await citizen.GetAsync("/api/auth/users");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Citizen_Cannot_Mutate_Shelters()
    {
        var citizen = Client(Roles.Citizen);
        var req = new CreateShelterRequest("Unauthorized Shelter", 23.7, 90.4, 50, 0, new List<string>(), ShelterStatus.Open);
        var res = await citizen.PostAsJsonAsync("/api/shelters", req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Citizen_Cannot_Publish_Emergency_Alerts()
    {
        var citizen = Client(Roles.Citizen);
        var req = new CreateAlertRequest("Fake Broadcast", "Fake Emergency", AlertSeverity.Catastrophic, null, "All", null, DateTimeOffset.UtcNow.AddHours(1));
        var res = await citizen.PostAsJsonAsync("/api/alerts", req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Rescuer_Cannot_Mutate_Warehouse_Resources()
    {
        var rescuer = Client(Roles.Rescuer);
        var req = new
        {
            Name = "Stolen Supplies",
            Type = ResourceType.Medicine,
            TotalQuantity = 100,
            AllocatedQuantity = 0,
            Unit = "kits",
            WarehouseLocation = "Central Hub",
            Latitude = 23.7,
            Longitude = 90.4
        };
        var res = await rescuer.PostAsJsonAsync("/api/relief/resources", req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Admin_Cannot_Lock_Own_Account()
    {
        var admin = Client("Admin");
        var adminId = Guid.Parse("33333333-3333-3333-3333-333333333334"); // FakeAuth Admin GUID
        var res = await admin.PostAsJsonAsync($"/api/auth/users/{adminId}/lock", new SetLockRequest(true));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Admin_Cannot_Modify_Own_Roles()
    {
        var admin = Client("Admin");
        var adminId = Guid.Parse("33333333-3333-3333-3333-333333333334");
        var res = await admin.PutAsJsonAsync($"/api/auth/users/{adminId}/roles", new SetRolesRequest(new[] { Roles.Citizen }));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Duplicate_Submission_With_IdempotencyKey_Returns_Existing_Record()
    {
        await ResetStateAsync();
        var citizen = Client(Roles.Citizen);
        var key = Guid.NewGuid().ToString("N");

        var req = new CreateIncidentRequest(
            Title: "Duplicate Idempotent Incident",
            Description: "Testing idempotency deduplication with unique key.",
            DisasterType: DisasterType.Flood,
            Severity: AlertSeverity.Minimal,
            Latitude: 23.8,
            Longitude: 90.4,
            AddressOrArea: null,
            AffectedPeopleCount: 1,
            IsSos: false,
            ContactPhone: null,
            PhotoPaths: null,
            IdempotencyKey: key
        );

        // First post -> Created 201
        var res1 = await citizen.PostAsJsonAsync("/api/incidents", req);
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);
        var body1 = await res1.Content.ReadFromJsonAsync<ApiEnvelope<IncidentDto>>();

        // Second post -> OK 200 with same Id
        var res2 = await citizen.PostAsJsonAsync("/api/incidents", req);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);
        var body2 = await res2.Content.ReadFromJsonAsync<ApiEnvelope<IncidentDto>>();

        Assert.Equal(body1!.Data.Id, body2!.Data.Id);
    }

    [Fact]
    public async Task Cross_User_Assistant_Chat_History_Is_Strictly_Isolated()
    {
        await ResetStateAsync();
        var citizen = Client(Roles.Citizen);
        var gov = Client(Roles.Government);
        var sharedSessionId = Guid.NewGuid();

        // 1. Citizen posts a message in session
        var citizenMsgRes = await citizen.PostAsJsonAsync("/api/ai/assistant/messages", new AssistantMessageRequest(
            SessionId: sharedSessionId,
            Message: "Private Citizen Question: Where is the nearest food station?",
            Latitude: null,
            Longitude: null
        ));
        Assert.Equal(HttpStatusCode.OK, citizenMsgRes.StatusCode);

        // 2. Government caller requests history for the same sessionId -> should return empty history
        var govHistoryRes = await gov.GetAsync($"/api/ai/assistant/sessions/{sharedSessionId}/messages");
        Assert.Equal(HttpStatusCode.OK, govHistoryRes.StatusCode);
        var govHistory = await govHistoryRes.Content.ReadFromJsonAsync<ApiEnvelope<AssistantHistoryResponse>>();
        Assert.NotNull(govHistory);
        Assert.Empty(govHistory.Data.Messages);
    }
}
