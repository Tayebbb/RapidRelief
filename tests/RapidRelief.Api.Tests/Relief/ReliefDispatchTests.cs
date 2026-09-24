using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RapidRelief.Api.Features.Audit.Data;
using RapidRelief.Api.Features.Realtime.Data;
using RapidRelief.Api.Features.Relief.Data;
using RapidRelief.Api.Features.Relief.Domain;
using RapidRelief.Api.Features.Relief.Endpoints;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using Xunit;

namespace RapidRelief.Api.Tests.Relief;

public sealed class ReliefDispatchTests : IClassFixture<TestingWebAppFactory>
{
    private const string RequestsPath = "/api/relief/requests";
    private const string ResourcesPath = "/api/relief/resources";

    private readonly TestingWebAppFactory _factory;

    public ReliefDispatchTests(TestingWebAppFactory factory) => _factory = factory;

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
        var audit = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        await relief.Dispatches.ExecuteDeleteAsync();
        await relief.Requests.ExecuteDeleteAsync();
        await relief.Resources.ExecuteDeleteAsync();
        await notifications.Reads.ExecuteDeleteAsync();
        await notifications.Notifications.ExecuteDeleteAsync();
        await audit.Entries.ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Happy_path_dispatch_and_delivery_updates_stock_request_status_and_audit()
    {
        await ResetAsync();

        var citizen = Client(Roles.Citizen);
        var gov = Client(Roles.Government);
        var rescuer = Client(Roles.Rescuer);

        // 1. Create request
        var reqResp = await citizen.PostAsJsonAsync(RequestsPath, new CreateReliefRequest(
            Type: ResourceType.Water,
            Quantity: 20,
            RecipientCount: 4,
            Urgency: "High",
            Latitude: 23.81,
            Longitude: 90.41,
            DeliveryAddress: "Dhanmondi Rd 8",
            Notes: "Bottled water needed",
            IncidentId: null,
            IdempotencyKey: null));
        Assert.Equal(HttpStatusCode.Created, reqResp.StatusCode);
        var req = (await reqResp.Content.ReadFromJsonAsync<ApiEnvelope<ReliefRequestDto>>())!.Data!;

        // 2. Approve and Allocate
        var appResp = await gov.PostAsJsonAsync($"{RequestsPath}/{req.Id}/status", new UpdateReliefStatusRequest(ReliefStatus.Approved, null));
        Assert.Equal(HttpStatusCode.OK, appResp.StatusCode);
        var allocResp = await gov.PostAsJsonAsync($"{RequestsPath}/{req.Id}/status", new UpdateReliefStatusRequest(ReliefStatus.Allocated, null));
        Assert.Equal(HttpStatusCode.OK, allocResp.StatusCode);

        // 3. Create resource
        var resResp = await gov.PostAsJsonAsync(ResourcesPath, new ReliefResourceRequest(
            Name: "Pure Water Jugs 20L",
            Category: ResourceType.Water,
            TotalQuantity: 100,
            AllocatedQuantity: 0,
            Unit: "Jugs",
            WarehouseLocation: "Depot West"));
        Assert.Equal(HttpStatusCode.Created, resResp.StatusCode);
        var res = (await resResp.Content.ReadFromJsonAsync<ApiEnvelope<ReliefResourceDto>>())!.Data!;

        // 4. Dispatch supplies
        var dispResp = await gov.PostAsJsonAsync($"{RequestsPath}/{req.Id}/dispatch", new DispatchRequest(
            ResourceId: res.Id,
            DispatchedQuantity: 20,
            CarrierOrPartner: "Volunteer Logistics Unit 1"));
        Assert.Equal(HttpStatusCode.Created, dispResp.StatusCode);
        var dispatch = (await dispResp.Content.ReadFromJsonAsync<ApiEnvelope<ReliefDispatchDto>>())!.Data!;
        Assert.Equal("Dispatched", dispatch.Status);
        Assert.Equal(20, dispatch.DispatchedQuantity);

        // Verify resource allocated stock increased
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ReliefDbContext>();
            var updatedRes = await db.Resources.FindAsync(res.Id);
            Assert.NotNull(updatedRes);
            Assert.Equal(20, updatedRes.AllocatedQuantity);

            var updatedReq = await db.Requests.FindAsync(req.Id);
            Assert.NotNull(updatedReq);
            Assert.Equal(ReliefStatus.Dispatched, updatedReq.Status);
        }

        // Query request dispatches
        var reqDispatchesResp = await citizen.GetAsync($"{RequestsPath}/{req.Id}/dispatches");
        Assert.Equal(HttpStatusCode.OK, reqDispatchesResp.StatusCode);
        var reqDispatches = (await reqDispatchesResp.Content.ReadFromJsonAsync<ApiEnvelope<IReadOnlyList<ReliefDispatchDto>>>())!.Data!;
        Assert.Single(reqDispatches);
        Assert.Equal(dispatch.Id, reqDispatches[0].Id);

        // Query resource dispatches
        var resDispatchesResp = await gov.GetAsync($"{ResourcesPath}/{res.Id}/dispatches");
        Assert.Equal(HttpStatusCode.OK, resDispatchesResp.StatusCode);
        var resDispatches = (await resDispatchesResp.Content.ReadFromJsonAsync<ApiEnvelope<PagedResult<ReliefDispatchDto>>>())!.Data!;
        Assert.Single(resDispatches.Items);

        // 5. Deliver dispatch via Rescuer
        var deliverResp = await rescuer.PostAsync($"/api/relief/dispatches/{dispatch.Id}/deliver", null);
        Assert.Equal(HttpStatusCode.OK, deliverResp.StatusCode);
        var delivered = (await deliverResp.Content.ReadFromJsonAsync<ApiEnvelope<ReliefDispatchDto>>())!.Data!;
        Assert.Equal("Delivered", delivered.Status);
        Assert.NotNull(delivered.DeliveredAtUtc);

        // Verify request is now Delivered
        var finalReqResp = await gov.GetAsync($"{RequestsPath}/{req.Id}");
        var finalReq = (await finalReqResp.Content.ReadFromJsonAsync<ApiEnvelope<ReliefRequestDto>>())!.Data!;
        Assert.Equal(ReliefStatus.Delivered, finalReq.Status);

        // Verify Audit records
        using (var scope = _factory.Services.CreateScope())
        {
            var audit = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
            var entries = await audit.Entries.Where(e => e.EntityType == "ReliefDispatch").ToListAsync();
            Assert.Contains(entries, e => e.Action == "Relief.Dispatch" && e.EntityId == dispatch.Id.ToString());
            Assert.Contains(entries, e => e.Action == "Relief.Deliver" && e.EntityId == dispatch.Id.ToString());
        }
    }

    [Fact]
    public async Task Multi_dispatch_marks_request_delivered_only_when_all_dispatches_are_delivered()
    {
        await ResetAsync();

        var citizen = Client(Roles.Citizen);
        var gov = Client(Roles.Government);
        var rescuer = Client(Roles.Rescuer);

        var reqResp = await citizen.PostAsJsonAsync(RequestsPath, new CreateReliefRequest(
            Type: ResourceType.Food,
            Quantity: 50,
            RecipientCount: 10,
            Urgency: "High",
            Latitude: 23.81,
            Longitude: 90.41,
            DeliveryAddress: "Sector 3",
            Notes: "Family meals",
            IncidentId: null,
            IdempotencyKey: null));
        var req = (await reqResp.Content.ReadFromJsonAsync<ApiEnvelope<ReliefRequestDto>>())!.Data!;

        await gov.PostAsJsonAsync($"{RequestsPath}/{req.Id}/status", new UpdateReliefStatusRequest(ReliefStatus.Approved, null));
        await gov.PostAsJsonAsync($"{RequestsPath}/{req.Id}/status", new UpdateReliefStatusRequest(ReliefStatus.Allocated, null));

        var resResp = await gov.PostAsJsonAsync(ResourcesPath, new ReliefResourceRequest(
            Name: "MRE Rations Pack",
            Category: ResourceType.Food,
            TotalQuantity: 100,
            AllocatedQuantity: 0,
            Unit: "Boxes",
            WarehouseLocation: "Depot North"));
        var res = (await resResp.Content.ReadFromJsonAsync<ApiEnvelope<ReliefResourceDto>>())!.Data!;

        // First dispatch: 20 boxes
        var disp1Resp = await gov.PostAsJsonAsync($"{RequestsPath}/{req.Id}/dispatch", new DispatchRequest(res.Id, 20, "Truck Alpha"));
        Assert.Equal(HttpStatusCode.Created, disp1Resp.StatusCode);
        var disp1 = (await disp1Resp.Content.ReadFromJsonAsync<ApiEnvelope<ReliefDispatchDto>>())!.Data!;

        // Second dispatch: 30 boxes
        var disp2Resp = await gov.PostAsJsonAsync($"{RequestsPath}/{req.Id}/dispatch", new DispatchRequest(res.Id, 30, "Truck Beta"));
        Assert.Equal(HttpStatusCode.Created, disp2Resp.StatusCode);
        var disp2 = (await disp2Resp.Content.ReadFromJsonAsync<ApiEnvelope<ReliefDispatchDto>>())!.Data!;

        // Deliver first dispatch only
        var del1Resp = await rescuer.PostAsync($"/api/relief/dispatches/{disp1.Id}/deliver", null);
        Assert.Equal(HttpStatusCode.OK, del1Resp.StatusCode);

        // Request must STILL be Dispatched because disp2 is pending
        var midReqResp = await gov.GetAsync($"{RequestsPath}/{req.Id}");
        var midReq = (await midReqResp.Content.ReadFromJsonAsync<ApiEnvelope<ReliefRequestDto>>())!.Data!;
        Assert.Equal(ReliefStatus.Dispatched, midReq.Status);

        // Deliver second dispatch
        var del2Resp = await rescuer.PostAsync($"/api/relief/dispatches/{disp2.Id}/deliver", null);
        Assert.Equal(HttpStatusCode.OK, del2Resp.StatusCode);

        // Now all dispatches are delivered -> Request must be Delivered
        var finalReqResp = await gov.GetAsync($"{RequestsPath}/{req.Id}");
        var finalReq = (await finalReqResp.Content.ReadFromJsonAsync<ApiEnvelope<ReliefRequestDto>>())!.Data!;
        Assert.Equal(ReliefStatus.Delivered, finalReq.Status);
    }

    [Fact]
    public async Task Insufficient_stock_returns_400_bad_request()
    {
        await ResetAsync();
        var citizen = Client(Roles.Citizen);
        var gov = Client(Roles.Government);

        var reqResp = await citizen.PostAsJsonAsync(RequestsPath, new CreateReliefRequest(
            Type: ResourceType.Medicine,
            Quantity: 50,
            RecipientCount: 5,
            Urgency: "High",
            Latitude: 23.81,
            Longitude: 90.41,
            DeliveryAddress: "Clinic",
            Notes: null,
            IncidentId: null,
            IdempotencyKey: null));
        var req = (await reqResp.Content.ReadFromJsonAsync<ApiEnvelope<ReliefRequestDto>>())!.Data!;

        await gov.PostAsJsonAsync($"{RequestsPath}/{req.Id}/status", new UpdateReliefStatusRequest(ReliefStatus.Approved, null));
        await gov.PostAsJsonAsync($"{RequestsPath}/{req.Id}/status", new UpdateReliefStatusRequest(ReliefStatus.Allocated, null));

        var resResp = await gov.PostAsJsonAsync(ResourcesPath, new ReliefResourceRequest(
            Name: "Bandages",
            Category: ResourceType.Medicine,
            TotalQuantity: 10,
            AllocatedQuantity: 0,
            Unit: "Rolls",
            WarehouseLocation: "Depot"));
        var res = (await resResp.Content.ReadFromJsonAsync<ApiEnvelope<ReliefResourceDto>>())!.Data!;

        // Available is 10, attempt to dispatch 25
        var dispResp = await gov.PostAsJsonAsync($"{RequestsPath}/{req.Id}/dispatch", new DispatchRequest(res.Id, 25, "Courier"));
        Assert.Equal(HttpStatusCode.BadRequest, dispResp.StatusCode);
    }

    [Fact]
    public async Task Dispatch_when_request_is_not_allocated_or_dispatched_returns_409_conflict()
    {
        await ResetAsync();
        var citizen = Client(Roles.Citizen);
        var gov = Client(Roles.Government);

        var reqResp = await citizen.PostAsJsonAsync(RequestsPath, new CreateReliefRequest(
            Type: ResourceType.Shelter,
            Quantity: 2,
            RecipientCount: 8,
            Urgency: "High",
            Latitude: 23.81,
            Longitude: 90.41,
            DeliveryAddress: "Camp",
            Notes: null,
            IncidentId: null,
            IdempotencyKey: null));
        var req = (await reqResp.Content.ReadFromJsonAsync<ApiEnvelope<ReliefRequestDto>>())!.Data!;

        var resResp = await gov.PostAsJsonAsync(ResourcesPath, new ReliefResourceRequest(
            Name: "Tents",
            Category: ResourceType.Shelter,
            TotalQuantity: 50,
            AllocatedQuantity: 0,
            Unit: "Tents",
            WarehouseLocation: "Depot"));
        var res = (await resResp.Content.ReadFromJsonAsync<ApiEnvelope<ReliefResourceDto>>())!.Data!;

        // Request is still Pending -> 409 Conflict
        var dispResp = await gov.PostAsJsonAsync($"{RequestsPath}/{req.Id}/dispatch", new DispatchRequest(res.Id, 2, "Van"));
        Assert.Equal(HttpStatusCode.Conflict, dispResp.StatusCode);
    }

    [Fact]
    public async Task Double_delivery_returns_409_conflict()
    {
        await ResetAsync();
        var citizen = Client(Roles.Citizen);
        var gov = Client(Roles.Government);
        var rescuer = Client(Roles.Rescuer);

        var reqResp = await citizen.PostAsJsonAsync(RequestsPath, new CreateReliefRequest(
            Type: ResourceType.Water,
            Quantity: 10,
            RecipientCount: 2,
            Urgency: "Medium",
            Latitude: 23.81,
            Longitude: 90.41,
            DeliveryAddress: "Home",
            Notes: null,
            IncidentId: null,
            IdempotencyKey: null));
        var req = (await reqResp.Content.ReadFromJsonAsync<ApiEnvelope<ReliefRequestDto>>())!.Data!;

        await gov.PostAsJsonAsync($"{RequestsPath}/{req.Id}/status", new UpdateReliefStatusRequest(ReliefStatus.Approved, null));
        await gov.PostAsJsonAsync($"{RequestsPath}/{req.Id}/status", new UpdateReliefStatusRequest(ReliefStatus.Allocated, null));

        var resResp = await gov.PostAsJsonAsync(ResourcesPath, new ReliefResourceRequest(
            Name: "Water Bottles",
            Category: ResourceType.Water,
            TotalQuantity: 50,
            AllocatedQuantity: 0,
            Unit: "Bottles",
            WarehouseLocation: "Depot"));
        var res = (await resResp.Content.ReadFromJsonAsync<ApiEnvelope<ReliefResourceDto>>())!.Data!;

        var dispResp = await gov.PostAsJsonAsync($"{RequestsPath}/{req.Id}/dispatch", new DispatchRequest(res.Id, 10, "Courier"));
        var disp = (await dispResp.Content.ReadFromJsonAsync<ApiEnvelope<ReliefDispatchDto>>())!.Data!;

        // First delivery -> 200 OK
        var deliv1 = await rescuer.PostAsync($"/api/relief/dispatches/{disp.Id}/deliver", null);
        Assert.Equal(HttpStatusCode.OK, deliv1.StatusCode);

        // Second delivery -> 409 Conflict
        var deliv2 = await rescuer.PostAsync($"/api/relief/dispatches/{disp.Id}/deliver", null);
        Assert.Equal(HttpStatusCode.Conflict, deliv2.StatusCode);
    }

    [Fact]
    public async Task Dispatch_and_delivery_enforce_role_authorization()
    {
        await ResetAsync();
        var citizen = Client(Roles.Citizen);
        var anonymous = Client();

        // Anonymous -> 401
        var unauthDisp = await anonymous.PostAsJsonAsync($"{RequestsPath}/{Guid.NewGuid()}/dispatch", new DispatchRequest(Guid.NewGuid(), 10, "Van"));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthDisp.StatusCode);

        var unauthDeliv = await anonymous.PostAsync($"/api/relief/dispatches/{Guid.NewGuid()}/deliver", null);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthDeliv.StatusCode);

        // Citizen dispatch -> 403 (Government required)
        var citDisp = await citizen.PostAsJsonAsync($"{RequestsPath}/{Guid.NewGuid()}/dispatch", new DispatchRequest(Guid.NewGuid(), 10, "Van"));
        Assert.Equal(HttpStatusCode.Forbidden, citDisp.StatusCode);

        // Citizen deliver -> 403 (Responder required)
        var citDeliv = await citizen.PostAsync($"/api/relief/dispatches/{Guid.NewGuid()}/deliver", null);
        Assert.Equal(HttpStatusCode.Forbidden, citDeliv.StatusCode);

        // Citizen access to resource dispatches -> 403 (Government required)
        var citResDispatches = await citizen.GetAsync($"{ResourcesPath}/{Guid.NewGuid()}/dispatches");
        Assert.Equal(HttpStatusCode.Forbidden, citResDispatches.StatusCode);
    }
}
