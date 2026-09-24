using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RapidRelief.Api.Features.Realtime.Data;
using RapidRelief.Api.Features.Relief.Data;
using RapidRelief.Api.Features.Relief.Domain;
using RapidRelief.Api.Features.Relief.Endpoints;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Tests.Relief;

public sealed class ReliefComprehensiveTests : IClassFixture<TestingWebAppFactory>
{
    private const string RequestsPath = "/api/relief/requests";
    private const string ResourcesPath = "/api/relief/resources";

    private readonly TestingWebAppFactory _factory;

    public ReliefComprehensiveTests(TestingWebAppFactory factory) => _factory = factory;

    private HttpClient Client(string? role = null)
    {
        var client = _factory.CreateClient();
        if (role is not null)
        {
            client.DefaultRequestHeaders.Add(FakeAuthHandler.HeaderName, role);
        }
        return client;
    }

    private async Task ResetAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var relief = scope.ServiceProvider.GetRequiredService<ReliefDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();

        await relief.Requests.ExecuteDeleteAsync();
        await relief.Resources.ExecuteDeleteAsync();
        await notifications.Reads.ExecuteDeleteAsync();
        await notifications.Notifications.ExecuteDeleteAsync();
    }

    // ==========================================
    // NORMAL TESTS
    // ==========================================

    [Theory]
    [InlineData(ResourceType.Water)]
    [InlineData(ResourceType.Food)]
    [InlineData(ResourceType.Medicine)]
    [InlineData(ResourceType.Shelter)]
    [InlineData(ResourceType.Clothing)]
    [InlineData(ResourceType.Other)]
    public async Task Citizen_can_create_relief_request_for_all_resource_types(ResourceType type)
    {
        await ResetAsync();

        var payload = new CreateReliefRequest(
            Type: type,
            Quantity: 10,
            RecipientCount: 5,
            Urgency: "High",
            Latitude: 23.8103,
            Longitude: 90.4125,
            DeliveryAddress: "House 12, Road 4, Mirpur",
            Notes: "Need immediate assistance for family",
            IncidentId: null,
            IdempotencyKey: null);

        var response = await Client(Roles.Citizen).PostAsJsonAsync(RequestsPath, payload);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<ReliefRequestDto>>();
        Assert.NotNull(envelope?.Data);
        Assert.Equal(type, envelope.Data.Type);
        Assert.Equal(ReliefStatus.Pending, envelope.Data.Status);
        Assert.Equal(10, envelope.Data.Quantity);
    }

    [Fact]
    public async Task Citizen_can_list_their_own_requests()
    {
        await ResetAsync();

        var client = Client(Roles.Citizen);
        var createResponse = await client.PostAsJsonAsync(RequestsPath, new CreateReliefRequest(
            Type: ResourceType.Food,
            Quantity: 20,
            RecipientCount: 4,
            Urgency: "Medium",
            Latitude: 23.8103,
            Longitude: 90.4125,
            DeliveryAddress: "Dhaka",
            Notes: "Rice and lentils",
            IncidentId: null,
            IdempotencyKey: null));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var listResponse = await client.GetAsync($"{RequestsPath}/mine");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var envelope = await listResponse.Content.ReadFromJsonAsync<ApiEnvelope<PagedResult<ReliefRequestDto>>>();
        Assert.NotNull(envelope?.Data);
        Assert.Single(envelope.Data.Items);
        Assert.Equal(ResourceType.Food, envelope.Data.Items[0].Type);
    }

    [Fact]
    public async Task Government_can_transition_request_through_full_lifecycle_and_citizen_is_notified()
    {
        await ResetAsync();

        var citizenClient = Client(Roles.Citizen);
        var govClient = Client(Roles.Government);

        var createResponse = await citizenClient.PostAsJsonAsync(RequestsPath, new CreateReliefRequest(
            Type: ResourceType.Medicine,
            Quantity: 5,
            RecipientCount: 2,
            Urgency: "Critical",
            Latitude: 23.8103,
            Longitude: 90.4125,
            DeliveryAddress: "Emergency Ward",
            Notes: "Insulin and bandages",
            IncidentId: null,
            IdempotencyKey: null));
        var created = (await createResponse.Content.ReadFromJsonAsync<ApiEnvelope<ReliefRequestDto>>())!.Data!;

        // Pending -> Approved
        var appResp = await govClient.PostAsJsonAsync($"{RequestsPath}/{created.Id}/status", new UpdateReliefStatusRequest(ReliefStatus.Approved, "Approved for dispatch"));
        Assert.Equal(HttpStatusCode.OK, appResp.StatusCode);

        // Approved -> Allocated
        var allocResp = await govClient.PostAsJsonAsync($"{RequestsPath}/{created.Id}/status", new UpdateReliefStatusRequest(ReliefStatus.Allocated, "Stock allocated at Central Hub"));
        Assert.Equal(HttpStatusCode.OK, allocResp.StatusCode);

        // Direct Allocated -> Dispatched via status endpoint is forbidden (D-118)
        var illegalDisp = await govClient.PostAsJsonAsync($"{RequestsPath}/{created.Id}/status", new UpdateReliefStatusRequest(ReliefStatus.Dispatched, "Direct dispatch"));
        Assert.Equal(HttpStatusCode.Conflict, illegalDisp.StatusCode);

        // Create warehouse resource for Medicine
        var createResResp = await govClient.PostAsJsonAsync(ResourcesPath, new ReliefResourceRequest(
            Name: "First Aid & Medicine Packs",
            Category: ResourceType.Medicine,
            TotalQuantity: 100,
            AllocatedQuantity: 0,
            Unit: "Packs",
            WarehouseLocation: "Central Hub Depot"));
        Assert.Equal(HttpStatusCode.Created, createResResp.StatusCode);
        var resDto = (await createResResp.Content.ReadFromJsonAsync<ApiEnvelope<ReliefResourceDto>>())!.Data!;

        // Allocated -> Dispatched via dedicated endpoint
        var dispResp = await govClient.PostAsJsonAsync($"{RequestsPath}/{created.Id}/dispatch", new DispatchRequest(
            ResourceId: resDto.Id,
            DispatchedQuantity: 5,
            CarrierOrPartner: "Red Crescent Courier"));
        Assert.Equal(HttpStatusCode.Created, dispResp.StatusCode);
        var dispDto = (await dispResp.Content.ReadFromJsonAsync<ApiEnvelope<ReliefDispatchDto>>())!.Data!;

        // Direct Dispatched -> Delivered via status endpoint is forbidden (D-118)
        var illegalDeliv = await govClient.PostAsJsonAsync($"{RequestsPath}/{created.Id}/status", new UpdateReliefStatusRequest(ReliefStatus.Delivered, "Direct deliver"));
        Assert.Equal(HttpStatusCode.Conflict, illegalDeliv.StatusCode);

        // Dispatched -> Delivered via dedicated endpoint
        var delivResp = await govClient.PostAsync($"/api/relief/dispatches/{dispDto.Id}/deliver", null);
        Assert.Equal(HttpStatusCode.OK, delivResp.StatusCode);

        // Verify final state
        var getResp = await govClient.GetAsync($"{RequestsPath}/{created.Id}");
        var finalDto = (await getResp.Content.ReadFromJsonAsync<ApiEnvelope<ReliefRequestDto>>())!.Data!;
        Assert.Equal(ReliefStatus.Delivered, finalDto.Status);

        // Verify notifications in DB
        using var scope = _factory.Services.CreateScope();
        var notifs = await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Notifications
            .Where(n => n.Topic == ReliefEndpoints.StatusTopic && n.UserId == created.RequesterId)
            .ToListAsync();
        Assert.NotEmpty(notifs);
    }

    [Fact]
    public async Task Warehouse_resource_management_lifecycle_and_open_demand_aggregation()
    {
        await ResetAsync();
        var govClient = Client(Roles.Government);

        // 1. Create a relief request to establish demand
        await Client(Roles.Citizen).PostAsJsonAsync(RequestsPath, new CreateReliefRequest(
            Type: ResourceType.Water,
            Quantity: 50,
            RecipientCount: 10,
            Urgency: "High",
            Latitude: 23.8,
            Longitude: 90.4,
            DeliveryAddress: "Sector 1",
            Notes: "Bottled water",
            IncidentId: null,
            IdempotencyKey: null));

        // 2. Create resource in warehouse
        var createResResp = await govClient.PostAsJsonAsync(ResourcesPath, new ReliefResourceRequest(
            Name: "Purified Water Packs",
            Category: ResourceType.Water,
            TotalQuantity: 1000,
            AllocatedQuantity: 100,
            Unit: "Liters",
            WarehouseLocation: "Warehouse A - Bay 3"));
        Assert.Equal(HttpStatusCode.Created, createResResp.StatusCode);
        var resDto = (await createResResp.Content.ReadFromJsonAsync<ApiEnvelope<ReliefResourceDto>>())!.Data!;
        Assert.Equal(900, resDto.AvailableQuantity);

        // 3. List inventory and verify open demand calculation
        var listResp = await govClient.GetAsync(ResourcesPath);
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var invEnvelope = await listResp.Content.ReadFromJsonAsync<ApiEnvelope<ReliefInventoryDto>>();
        Assert.NotNull(invEnvelope?.Data);
        var item = invEnvelope.Data.Items.Single(x => x.Category == ResourceType.Water);
        Assert.Equal(50, item.OpenDemand);
        Assert.Equal(1000, item.TotalQuantity);
        Assert.Equal(100, item.AllocatedQuantity);
        Assert.Equal(900, item.AvailableQuantity);

        // 4. Update resource stock
        var updateResp = await govClient.PutAsJsonAsync($"{ResourcesPath}/{resDto.Id}", new ReliefResourceRequest(
            Name: "Purified Water Packs (Updated)",
            Category: ResourceType.Water,
            TotalQuantity: 1200,
            AllocatedQuantity: 150,
            Unit: "Liters",
            WarehouseLocation: "Warehouse A - Bay 4"));
        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);
        var updatedDto = (await updateResp.Content.ReadFromJsonAsync<ApiEnvelope<ReliefResourceDto>>())!.Data!;
        Assert.Equal(1050, updatedDto.AvailableQuantity);
    }

    // ==========================================
    // BOUNDARY TESTS
    // ==========================================

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(1000, 500, true)]
    [InlineData(0, 5, false)]
    [InlineData(1001, 5, false)]
    [InlineData(10, 0, false)]
    [InlineData(10, 501, false)]
    public async Task Relief_request_quantity_and_recipient_boundary_validation(int quantity, int recipients, bool shouldSucceed)
    {
        await ResetAsync();

        var payload = new CreateReliefRequest(
            Type: ResourceType.Food,
            Quantity: quantity,
            RecipientCount: recipients,
            Urgency: "High",
            Latitude: 23.8,
            Longitude: 90.4,
            DeliveryAddress: "Dhaka",
            Notes: "Boundary test",
            IncidentId: null,
            IdempotencyKey: null);

        var response = await Client(Roles.Citizen).PostAsJsonAsync(RequestsPath, payload);
        if (shouldSucceed)
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Theory]
    [InlineData(-90, -180, true)]
    [InlineData(90, 180, true)]
    [InlineData(-90.001, 0, false)]
    [InlineData(90.001, 0, false)]
    [InlineData(0, -180.001, false)]
    [InlineData(0, 180.001, false)]
    public async Task Relief_request_geo_coordinate_boundaries(double lat, double lng, bool shouldSucceed)
    {
        await ResetAsync();

        var payload = new CreateReliefRequest(
            Type: ResourceType.Water,
            Quantity: 5,
            RecipientCount: 2,
            Urgency: "Normal",
            Latitude: lat,
            Longitude: lng,
            DeliveryAddress: "Coordinates Test",
            Notes: "Geo boundary check",
            IncidentId: null,
            IdempotencyKey: null);

        var response = await Client(Roles.Citizen).PostAsJsonAsync(RequestsPath, payload);
        if (shouldSucceed)
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public async Task Relief_request_string_max_length_boundaries()
    {
        await ResetAsync();

        // Valid max lengths
        var validPayload = new CreateReliefRequest(
            Type: ResourceType.Water,
            Quantity: 10,
            RecipientCount: 5,
            Urgency: new string('U', 30),
            Latitude: 23.8,
            Longitude: 90.4,
            DeliveryAddress: new string('A', 250),
            Notes: new string('N', 1000),
            IncidentId: null,
            IdempotencyKey: new string('K', 80));

        var validResp = await Client(Roles.Citizen).PostAsJsonAsync(RequestsPath, validPayload);
        Assert.Equal(HttpStatusCode.Created, validResp.StatusCode);

        // Exceeding max length on Urgency (> 30)
        var invalidUrgency = validPayload with { Urgency = new string('U', 31), IdempotencyKey = "key1" };
        var resp1 = await Client(Roles.Citizen).PostAsJsonAsync(RequestsPath, invalidUrgency);
        Assert.Equal(HttpStatusCode.BadRequest, resp1.StatusCode);

        // Exceeding max length on DeliveryAddress (> 250)
        var invalidAddress = validPayload with { DeliveryAddress = new string('A', 251), IdempotencyKey = "key2" };
        var resp2 = await Client(Roles.Citizen).PostAsJsonAsync(RequestsPath, invalidAddress);
        Assert.Equal(HttpStatusCode.BadRequest, resp2.StatusCode);

        // Exceeding max length on Notes (> 1000)
        var invalidNotes = validPayload with { Notes = new string('N', 1001), IdempotencyKey = "key3" };
        var resp3 = await Client(Roles.Citizen).PostAsJsonAsync(RequestsPath, invalidNotes);
        Assert.Equal(HttpStatusCode.BadRequest, resp3.StatusCode);

        // Exceeding max length on IdempotencyKey (> 80)
        var invalidKey = validPayload with { IdempotencyKey = new string('K', 81) };
        var resp4 = await Client(Roles.Citizen).PostAsJsonAsync(RequestsPath, invalidKey);
        Assert.Equal(HttpStatusCode.BadRequest, resp4.StatusCode);
    }

    // ==========================================
    // INVALID & EXCEPTIONAL TESTS
    // ==========================================

    [Fact]
    public async Task Invalid_status_transitions_return_409_conflict()
    {
        await ResetAsync();

        var citizenClient = Client(Roles.Citizen);
        var govClient = Client(Roles.Government);

        var createResp = await citizenClient.PostAsJsonAsync(RequestsPath, new CreateReliefRequest(
            Type: ResourceType.Clothing,
            Quantity: 10,
            RecipientCount: 3,
            Urgency: "High",
            Latitude: 23.8,
            Longitude: 90.4,
            DeliveryAddress: "Dhaka",
            Notes: "Emergency clothes",
            IncidentId: null,
            IdempotencyKey: null));
        var created = (await createResp.Content.ReadFromJsonAsync<ApiEnvelope<ReliefRequestDto>>())!.Data!;

        // Pending -> Delivered is illegal (must go through Approved -> Allocated -> Dispatched)
        var illegalResp1 = await govClient.PostAsJsonAsync($"{RequestsPath}/{created.Id}/status",
            new UpdateReliefStatusRequest(ReliefStatus.Delivered, "Direct delivery attempt"));
        Assert.Equal(HttpStatusCode.Conflict, illegalResp1.StatusCode);

        // Advance to Dispatched
        await govClient.PostAsJsonAsync($"{RequestsPath}/{created.Id}/status", new UpdateReliefStatusRequest(ReliefStatus.Approved, null));
        await govClient.PostAsJsonAsync($"{RequestsPath}/{created.Id}/status", new UpdateReliefStatusRequest(ReliefStatus.Allocated, null));

        var createResResp = await govClient.PostAsJsonAsync(ResourcesPath, new ReliefResourceRequest(
            Name: "Emergency Clothing",
            Category: ResourceType.Clothing,
            TotalQuantity: 100,
            AllocatedQuantity: 0,
            Unit: "Items",
            WarehouseLocation: "Hub A"));
        Assert.Equal(HttpStatusCode.Created, createResResp.StatusCode);
        var resDto = (await createResResp.Content.ReadFromJsonAsync<ApiEnvelope<ReliefResourceDto>>())!.Data!;

        var dispResp = await govClient.PostAsJsonAsync($"{RequestsPath}/{created.Id}/dispatch", new DispatchRequest(
            ResourceId: resDto.Id,
            DispatchedQuantity: 10,
            CarrierOrPartner: "Red Crescent Courier"));
        Assert.Equal(HttpStatusCode.Created, dispResp.StatusCode);

        // Dispatched -> Rejected is illegal (cannot reject after leaving warehouse)
        var illegalResp2 = await govClient.PostAsJsonAsync($"{RequestsPath}/{created.Id}/status",
            new UpdateReliefStatusRequest(ReliefStatus.Rejected, "Reject after dispatch attempt"));
        Assert.Equal(HttpStatusCode.Conflict, illegalResp2.StatusCode);
    }

    [Fact]
    public async Task Idempotency_key_returns_exact_same_request_without_duplicate_creation()
    {
        await ResetAsync();
        var client = Client(Roles.Citizen);
        var key = "unique-relief-key-" + Guid.NewGuid();

        var payload = new CreateReliefRequest(
            Type: ResourceType.Clothing,
            Quantity: 15,
            RecipientCount: 5,
            Urgency: "Medium",
            Latitude: 23.8,
            Longitude: 90.4,
            DeliveryAddress: "Camp B",
            Notes: "Warm clothes",
            IncidentId: null,
            IdempotencyKey: key);

        var firstResp = await client.PostAsJsonAsync(RequestsPath, payload);
        Assert.Equal(HttpStatusCode.Created, firstResp.StatusCode);
        var firstDto = (await firstResp.Content.ReadFromJsonAsync<ApiEnvelope<ReliefRequestDto>>())!.Data!;

        // Re-post with same idempotency key
        var secondResp = await client.PostAsJsonAsync(RequestsPath, payload);
        Assert.Equal(HttpStatusCode.OK, secondResp.StatusCode);
        var secondDto = (await secondResp.Content.ReadFromJsonAsync<ApiEnvelope<ReliefRequestDto>>())!.Data!;

        Assert.Equal(firstDto.Id, secondDto.Id);

        // Ensure database only contains 1 record
        using var scope = _factory.Services.CreateScope();
        var count = await scope.ServiceProvider.GetRequiredService<ReliefDbContext>().Requests.CountAsync();
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Citizen_cannot_cancel_request_once_dispatched()
    {
        await ResetAsync();
        var citizenClient = Client(Roles.Citizen);
        var govClient = Client(Roles.Government);

        var createResp = await citizenClient.PostAsJsonAsync(RequestsPath, new CreateReliefRequest(
            Type: ResourceType.Other,
            Quantity: 10,
            RecipientCount: 2,
            Urgency: "Low",
            Latitude: 23.8,
            Longitude: 90.4,
            DeliveryAddress: "Dhaka",
            Notes: "Flashlights",
            IncidentId: null,
            IdempotencyKey: null));
        var created = (await createResp.Content.ReadFromJsonAsync<ApiEnvelope<ReliefRequestDto>>())!.Data!;

        // Advance to Dispatched
        await govClient.PostAsJsonAsync($"{RequestsPath}/{created.Id}/status", new UpdateReliefStatusRequest(ReliefStatus.Approved, null));
        await govClient.PostAsJsonAsync($"{RequestsPath}/{created.Id}/status", new UpdateReliefStatusRequest(ReliefStatus.Allocated, null));

        var createResResp = await govClient.PostAsJsonAsync(ResourcesPath, new ReliefResourceRequest(
            Name: "Emergency Flashlights",
            Category: ResourceType.Other,
            TotalQuantity: 100,
            AllocatedQuantity: 0,
            Unit: "Items",
            WarehouseLocation: "Hub B"));
        Assert.Equal(HttpStatusCode.Created, createResResp.StatusCode);
        var resDto = (await createResResp.Content.ReadFromJsonAsync<ApiEnvelope<ReliefResourceDto>>())!.Data!;

        var dispResp = await govClient.PostAsJsonAsync($"{RequestsPath}/{created.Id}/dispatch", new DispatchRequest(
            ResourceId: resDto.Id,
            DispatchedQuantity: 10,
            CarrierOrPartner: "Courier Express"));
        Assert.Equal(HttpStatusCode.Created, dispResp.StatusCode);

        // Citizen attempts cancel -> 409 Conflict
        var cancelResp = await citizenClient.PostAsync($"{RequestsPath}/{created.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.Conflict, cancelResp.StatusCode);
    }

    // ==========================================
    // SECURITY & RBAC TESTS
    // ==========================================

    [Fact]
    public async Task Unauthenticated_and_unauthorized_relief_access_is_rejected()
    {
        await ResetAsync();

        // Anonymous call to protected endpoint
        var anonResp = await Client().GetAsync($"{RequestsPath}/mine");
        Assert.Equal(HttpStatusCode.Unauthorized, anonResp.StatusCode);

        // Citizen attempting to access Government queue
        var citizenResp = await Client(Roles.Citizen).GetAsync(RequestsPath);
        Assert.Equal(HttpStatusCode.Forbidden, citizenResp.StatusCode);

        // Rescuer attempting to access Government inventory
        var rescuerResp = await Client(Roles.Rescuer).GetAsync(ResourcesPath);
        Assert.Equal(HttpStatusCode.Forbidden, rescuerResp.StatusCode);
    }
}
