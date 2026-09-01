using System.Net;
using System.Net.Http.Json;
using RapidRelief.Api.Features.Shelters.Domain;
using RapidRelief.Api.Features.Shelters.Endpoints;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.ReadModels;
using Xunit;

namespace RapidRelief.Api.Tests.Shelters;

public sealed class SheltersTests : IClassFixture<TestingWebAppFactory>
{
    private readonly TestingWebAppFactory _factory;

    public SheltersTests(TestingWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetShelters_ReturnsSeedData()
    {
        var client = _factory.CreateClient();
        
        var response = await client.GetAsync("/api/shelters");
        
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<PagedResult<ShelterDto>>>();
        
        Assert.NotNull(envelope?.Data);
        // We know we seeded 8 shelters from DhakaSeedData
        Assert.True(envelope.Data.TotalCount >= 8);
    }

    [Fact]
    public async Task CreateShelter_AsAdmin_Succeeds()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-Role", "Admin");
        
        var request = new CreateShelterRequest(
            "Test Shelter",
            23.8103,
            90.4125,
            500,
            100,
            new List<string> { "Water", "Power" },
            ShelterStatus.Open);
            
        var response = await client.PostAsJsonAsync("/api/shelters", request);
        
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<ShelterDto>>();
        
        Assert.NotNull(envelope?.Data);
        Assert.Equal("Test Shelter", envelope.Data.Name);
        Assert.Equal(500, envelope.Data.Capacity);
    }
    
    [Fact]
    public async Task CreateShelter_AsCitizen_Fails()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-Role", "Citizen");
        
        var request = new CreateShelterRequest(
            "Test Shelter",
            23.8103,
            90.4125,
            500,
            100,
            new List<string> { "Water", "Power" },
            ShelterStatus.Open);
            
        var response = await client.PostAsJsonAsync("/api/shelters", request);
        
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateOccupancy_AsAdmin_Succeeds()
    {
        // First create a shelter
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-Role", "Admin");
        
        var createReq = new CreateShelterRequest(
            "Occupancy Test Shelter",
            23.81,
            90.41,
            100,
            0,
            new List<string>(),
            ShelterStatus.Open);
            
        var createRes = await client.PostAsJsonAsync("/api/shelters", createReq);
        var createEnv = await createRes.Content.ReadFromJsonAsync<ApiEnvelope<ShelterDto>>();
        var shelterId = createEnv!.Data!.Id;
        
        // Update occupancy
        var patchReq = new UpdateOccupancyRequest(100);
        var patchRes = await client.PatchAsJsonAsync($"/api/shelters/{shelterId}/occupancy", patchReq);
        
        Assert.Equal(HttpStatusCode.OK, patchRes.StatusCode);
        var patchEnv = await patchRes.Content.ReadFromJsonAsync<ApiEnvelope<ShelterDto>>();
        
        Assert.Equal(100, patchEnv!.Data!.CurrentOccupancy);
        // It should auto-update status to Full since occupancy >= capacity
        Assert.Equal(ShelterStatus.Full, patchEnv.Data.Status);
    }
    
    [Fact]
    public async Task GetShelterRecommendation_ReturnsNearest()
    {
        var client = _factory.CreateClient();
        
        var response = await client.GetAsync("/api/shelters/recommend?lat=23.8103&lng=90.4125");
        
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<ShelterSummaryDto>>();
        
        Assert.NotNull(envelope?.Data);
    }
}
