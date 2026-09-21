using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RapidRelief.Api.Features.Shelters.Data;
using RapidRelief.Api.Features.Shelters.Domain;
using RapidRelief.Api.Features.Shelters.Endpoints;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Api.Tests.Shelters;

public sealed class SheltersComprehensiveTests : IClassFixture<TestingWebAppFactory>
{
    private const string BasePath = "/api/shelters";
    private readonly TestingWebAppFactory _factory;

    public SheltersComprehensiveTests(TestingWebAppFactory factory) => _factory = factory;

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
        var ops = scope.ServiceProvider.GetRequiredService<OpsDbContext>();
        await ops.Shelters.ExecuteDeleteAsync();
    }

    // ==========================================
    // NORMAL TESTS
    // ==========================================

    [Fact]
    public async Task Admin_can_manage_full_shelter_lifecycle()
    {
        await ResetAsync();
        var admin = Client(Roles.Admin);
        var anon = Client();

        // 1. Create Shelter
        var createPayload = new CreateShelterRequest(
            Name: "Mirpur Community Center Shelter",
            Latitude: 23.8041,
            Longitude: 90.3687,
            Capacity: 200,
            CurrentOccupancy: 50,
            Facilities: new List<string> { "Drinking Water", "Medical Support", "Power" },
            Status: ShelterStatus.Open);

        var createResp = await admin.PostAsJsonAsync(BasePath, createPayload);
        Assert.Equal(HttpStatusCode.Created, createResp.StatusCode);
        var created = (await createResp.Content.ReadFromJsonAsync<ApiEnvelope<ShelterDto>>())!.Data!;
        Assert.Equal("Mirpur Community Center Shelter", created.Name);
        Assert.Equal(200, created.Capacity);
        Assert.Equal(50, created.CurrentOccupancy);
        Assert.Equal(ShelterStatus.Open, created.Status);

        // 2. Read by ID as anonymous user
        var getResp = await anon.GetAsync($"{BasePath}/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var readDto = (await getResp.Content.ReadFromJsonAsync<ApiEnvelope<ShelterDto>>())!.Data!;
        Assert.Equal(created.Id, readDto.Id);

        // 3. Update Shelter details
        var updatePayload = new UpdateShelterRequest(
            Name: "Mirpur Mega Shelter",
            Latitude: 23.8041,
            Longitude: 90.3687,
            Capacity: 300,
            CurrentOccupancy: 80,
            Facilities: new List<string> { "Drinking Water", "Medical Support", "Power", "Kitchen" },
            Status: ShelterStatus.Open);

        var updateResp = await admin.PutAsJsonAsync($"{BasePath}/{created.Id}", updatePayload);
        Assert.Equal(HttpStatusCode.OK, updateResp.StatusCode);
        var updated = (await updateResp.Content.ReadFromJsonAsync<ApiEnvelope<ShelterDto>>())!.Data!;
        Assert.Equal("Mirpur Mega Shelter", updated.Name);
        Assert.Equal(300, updated.Capacity);
        Assert.Equal(80, updated.CurrentOccupancy);

        // 4. Update Occupancy to Capacity (auto-switches to Full)
        var occResp = await admin.PatchAsJsonAsync($"{BasePath}/{created.Id}/occupancy", new UpdateOccupancyRequest(300));
        Assert.Equal(HttpStatusCode.OK, occResp.StatusCode);
        var fullDto = (await occResp.Content.ReadFromJsonAsync<ApiEnvelope<ShelterDto>>())!.Data!;
        Assert.Equal(300, fullDto.CurrentOccupancy);
        Assert.Equal(ShelterStatus.Full, fullDto.Status);

        // 5. Update Occupancy below Capacity (auto-switches back to Open)
        var occResp2 = await admin.PatchAsJsonAsync($"{BasePath}/{created.Id}/occupancy", new UpdateOccupancyRequest(250));
        Assert.Equal(HttpStatusCode.OK, occResp2.StatusCode);
        var openDto = (await occResp2.Content.ReadFromJsonAsync<ApiEnvelope<ShelterDto>>())!.Data!;
        Assert.Equal(250, openDto.CurrentOccupancy);
        Assert.Equal(ShelterStatus.Open, openDto.Status);
    }

    [Fact]
    public async Task Anonymous_user_can_list_shelters_and_query_nearest_shelters()
    {
        await ResetAsync();
        var admin = Client(Roles.Admin);
        var anon = Client();

        // Seed 2 shelters
        await admin.PostAsJsonAsync(BasePath, new CreateShelterRequest("Shelter North", 23.85, 90.40, 100, 10, ["Water"], ShelterStatus.Open));
        await admin.PostAsJsonAsync(BasePath, new CreateShelterRequest("Shelter South", 23.70, 90.40, 100, 10, ["Water"], ShelterStatus.Open));

        // Normal list
        var listResp = await anon.GetAsync(BasePath);
        Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
        var listEnvelope = await listResp.Content.ReadFromJsonAsync<ApiEnvelope<PagedResult<ShelterDto>>>();
        Assert.NotNull(listEnvelope?.Data);
        Assert.Equal(2, listEnvelope.Data.TotalCount);

        // Nearest query
        var nearResp = await anon.GetAsync($"{BasePath}?lat=23.84&lng=90.40&pageSize=5");
        Assert.Equal(HttpStatusCode.OK, nearResp.StatusCode);
        var nearEnvelope = await nearResp.Content.ReadFromJsonAsync<ApiEnvelope<PagedResult<ShelterSummaryDto>>>();
        Assert.NotNull(nearEnvelope?.Data);
        Assert.NotEmpty(nearEnvelope.Data.Items);
    }

    // ==========================================
    // BOUNDARY & INVALID TESTS
    // ==========================================

    [Theory]
    [InlineData(-90, -180, 0, 0, true)]
    [InlineData(90, 180, 100, 100, true)]
    [InlineData(-90.001, 0, 100, 10, false)]
    [InlineData(90.001, 0, 100, 10, false)]
    [InlineData(0, -180.001, 100, 10, false)]
    [InlineData(0, 180.001, 100, 10, false)]
    [InlineData(0, 0, -1, 0, false)]
    [InlineData(0, 0, 50, 51, false)] // Occupancy > Capacity
    public async Task Shelter_create_validation_rules(double lat, double lng, int capacity, int occupancy, bool shouldSucceed)
    {
        await ResetAsync();
        var admin = Client(Roles.Admin);

        var payload = new CreateShelterRequest(
            Name: "Test Shelter",
            Latitude: lat,
            Longitude: lng,
            Capacity: capacity,
            CurrentOccupancy: occupancy,
            Facilities: new List<string> { "Bedding" },
            Status: ShelterStatus.Open);

        var resp = await admin.PostAsJsonAsync(BasePath, payload);
        if (shouldSucceed)
        {
            Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }
    }

    [Fact]
    public async Task Patch_occupancy_exceeding_capacity_is_rejected()
    {
        await ResetAsync();
        var admin = Client(Roles.Admin);

        var createResp = await admin.PostAsJsonAsync(BasePath, new CreateShelterRequest(
            Name: "Small Shelter",
            Latitude: 23.8,
            Longitude: 90.4,
            Capacity: 50,
            CurrentOccupancy: 10,
            Facilities: new List<string>(),
            Status: ShelterStatus.Open));
        var created = (await createResp.Content.ReadFromJsonAsync<ApiEnvelope<ShelterDto>>())!.Data!;

        // Attempt to patch occupancy to 51 when capacity is 50
        var patchResp = await admin.PatchAsJsonAsync($"{BasePath}/{created.Id}/occupancy", new UpdateOccupancyRequest(51));
        Assert.Equal(HttpStatusCode.BadRequest, patchResp.StatusCode);
    }

    // ==========================================
    // SECURITY & EXCEPTIONAL TESTS
    // ==========================================

    [Theory]
    [InlineData(Roles.Citizen)]
    [InlineData(Roles.Rescuer)]
    public async Task Non_admins_cannot_mutate_shelters(string nonAdminRole)
    {
        await ResetAsync();
        var client = Client(nonAdminRole);

        // Attempt create
        var createResp = await client.PostAsJsonAsync(BasePath, new CreateShelterRequest("Unauthorized Shelter", 23.8, 90.4, 100, 0, new(), ShelterStatus.Open));
        Assert.Equal(HttpStatusCode.Forbidden, createResp.StatusCode);

        // Attempt update
        var updateResp = await client.PutAsJsonAsync($"{BasePath}/{Guid.NewGuid()}", new UpdateShelterRequest("Unauthorized Shelter", 23.8, 90.4, 100, 0, new(), ShelterStatus.Open));
        Assert.Equal(HttpStatusCode.Forbidden, updateResp.StatusCode);

        // Attempt patch
        var patchResp = await client.PatchAsJsonAsync($"{BasePath}/{Guid.NewGuid()}/occupancy", new UpdateOccupancyRequest(10));
        Assert.Equal(HttpStatusCode.Forbidden, patchResp.StatusCode);
    }

    [Fact]
    public async Task Nonexistent_shelter_id_returns_404_not_found()
    {
        await ResetAsync();
        var admin = Client(Roles.Admin);
        var nonexistent = Guid.NewGuid();

        var getResp = await admin.GetAsync($"{BasePath}/{nonexistent}");
        Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);

        var putResp = await admin.PutAsJsonAsync($"{BasePath}/{nonexistent}", new UpdateShelterRequest("Test", 23.8, 90.4, 100, 10, new(), ShelterStatus.Open));
        Assert.Equal(HttpStatusCode.NotFound, putResp.StatusCode);

        var patchResp = await admin.PatchAsJsonAsync($"{BasePath}/{nonexistent}/occupancy", new UpdateOccupancyRequest(10));
        Assert.Equal(HttpStatusCode.NotFound, patchResp.StatusCode);
    }
}
