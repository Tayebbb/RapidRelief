using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RapidRelief.Api.Features.SafetyZones.Data;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Api.Tests.SafetyZones;

public sealed class SafetyZonesTests : IClassFixture<TestingWebAppFactory>
{
    private const string ZonesPath = "/api/safety/zones";
    private const string ClosuresPath = "/api/safety/closures";
    private readonly TestingWebAppFactory _factory;

    public SafetyZonesTests(TestingWebAppFactory factory)
    {
        _factory = factory;
    }

    private HttpClient Client(string? role = null)
    {
        var client = _factory.CreateClient();
        if (!string.IsNullOrEmpty(role))
        {
            client.DefaultRequestHeaders.Add(FakeAuthHandler.HeaderName, role);
        }
        return client;
    }

    private async Task ResetDatabaseAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SafetyZonesDbContext>();
        await db.SafetyZones.ExecuteDeleteAsync();
        await db.RoadClosures.ExecuteDeleteAsync();
    }

    [Fact]
    public async Task GetZones_AnonymousAccess_ReturnsActiveZones()
    {
        var client = Client();
        var response = await client.GetAsync(ZonesPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<SafetyZoneDto>>>();
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Data);
        Assert.NotEmpty(envelope.Data);
    }

    [Fact]
    public async Task GetClosures_AnonymousAccess_ReturnsActiveClosures()
    {
        var client = Client();
        var response = await client.GetAsync(ClosuresPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<List<RoadClosureDto>>>();
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Data);
        Assert.NotEmpty(envelope.Data);
    }

    [Fact]
    public async Task CreateZone_GovernmentRole_CreatesSuccessfully()
    {
        var client = Client(Roles.Government);
        var req = new CreateSafetyZoneRequest(
            "Gulshan Lake Inundation Zone",
            "Flooding detected along lake promenade",
            ZoneType.DangerZone,
            Severity.Severe,
            "Circle",
            23.7925,
            90.4078,
            600,
            null,
            DateTimeOffset.UtcNow.AddHours(12));

        var response = await client.PostAsJsonAsync(ZonesPath, req);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<SafetyZoneDto>>();
        Assert.NotNull(envelope?.Data);
        Assert.Equal("Gulshan Lake Inundation Zone", envelope.Data.Name);
        Assert.Equal(ZoneType.DangerZone, envelope.Data.ZoneType);
        Assert.Equal(600, envelope.Data.RadiusMeters);

        // Verify in database
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SafetyZonesDbContext>();
        var persisted = await db.SafetyZones.FindAsync(envelope.Data.Id);
        Assert.NotNull(persisted);
        Assert.Equal("Gulshan Lake Inundation Zone", persisted.Name);
    }

    [Fact]
    public async Task CreateZone_CitizenRole_ReturnsForbidden()
    {
        var client = Client(Roles.Citizen);
        var req = new CreateSafetyZoneRequest(
            "Unauthorized Zone",
            "Should not be created",
            ZoneType.DangerZone,
            Severity.Severe,
            "Circle",
            23.8103,
            90.4125,
            500,
            null,
            null);

        var response = await client.PostAsJsonAsync(ZonesPath, req);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateZone_Anonymous_ReturnsUnauthorized()
    {
        var client = Client();
        var req = new CreateSafetyZoneRequest(
            "Anonymous Zone",
            "Should not be created",
            ZoneType.DangerZone,
            Severity.Severe,
            "Circle",
            23.8103,
            90.4125,
            500,
            null,
            null);

        var response = await client.PostAsJsonAsync(ZonesPath, req);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateZone_InvalidCoordinates_ReturnsBadRequest()
    {
        var client = Client(Roles.Government);
        var req = new CreateSafetyZoneRequest(
            "Invalid Zone",
            "Invalid lat/lng",
            ZoneType.DangerZone,
            Severity.Severe,
            "Circle",
            120.0, // Invalid Latitude > 90
            90.4125,
            -10, // Invalid Radius < 0
            null,
            null);

        var response = await client.PostAsJsonAsync(ZonesPath, req);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateZone_GovernmentRole_UpdatesSuccessfully()
    {
        var client = Client(Roles.Government);
        var createReq = new CreateSafetyZoneRequest(
            "Temporary Shelter Perimeter",
            "Initial perimeter description",
            ZoneType.SafeAssemblyPoint,
            Severity.Moderate,
            "Circle",
            23.7500,
            90.3800,
            300,
            null,
            null);

        var createRes = await client.PostAsJsonAsync(ZonesPath, createReq);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var createdDto = (await createRes.Content.ReadFromJsonAsync<ApiEnvelope<SafetyZoneDto>>())!.Data;

        var updateReq = new UpdateSafetyZoneRequest(
            "Updated Shelter Perimeter",
            "Expanded shelter perimeter",
            ZoneType.SafeAssemblyPoint,
            Severity.Minor,
            "Circle",
            23.7500,
            90.3800,
            500,
            null,
            false,
            DateTimeOffset.UtcNow.AddDays(1));

        var updateRes = await client.PutAsJsonAsync($"{ZonesPath}/{createdDto.Id}", updateReq);
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);

        var updatedDto = (await updateRes.Content.ReadFromJsonAsync<ApiEnvelope<SafetyZoneDto>>())!.Data;
        Assert.Equal("Updated Shelter Perimeter", updatedDto.Name);
        Assert.Equal(500, updatedDto.RadiusMeters);
        Assert.False(updatedDto.IsActive);
    }

    [Fact]
    public async Task DeleteZone_GovernmentRole_DeactivatesOrRemoves()
    {
        var client = Client(Roles.Government);
        var createReq = new CreateSafetyZoneRequest(
            "Zone To Delete",
            "Will be deleted",
            ZoneType.RestrictedArea,
            Severity.Moderate,
            "Circle",
            23.7700,
            90.3900,
            200,
            null,
            null);

        var createRes = await client.PostAsJsonAsync(ZonesPath, createReq);
        var createdDto = (await createRes.Content.ReadFromJsonAsync<ApiEnvelope<SafetyZoneDto>>())!.Data;

        var deleteRes = await client.DeleteAsync($"{ZonesPath}/{createdDto.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteRes.StatusCode);

        // Fetch all active zones and verify it is not in active list
        var activeRes = await client.GetAsync(ZonesPath);
        var activeEnvelope = await activeRes.Content.ReadFromJsonAsync<ApiEnvelope<List<SafetyZoneDto>>>();
        Assert.DoesNotContain(activeEnvelope!.Data, z => z.Id == createdDto.Id);
    }

    [Fact]
    public async Task CreateRoadClosure_GovernmentRole_CreatesSuccessfully()
    {
        var client = Client(Roles.Government);
        var req = new CreateRoadClosureRequest(
            "Banani 11 Bridge",
            "Culvert collapse under heavy surge",
            ClosureSeverity.TotalClosure,
            "[[23.7937,90.4042],[23.7945,90.4080]]",
            "Culvert collapsed",
            "Use Kemal Ataturk Avenue",
            DateTimeOffset.UtcNow.AddHours(24));

        var response = await client.PostAsJsonAsync(ClosuresPath, req);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<RoadClosureDto>>();
        Assert.NotNull(envelope?.Data);
        Assert.Equal("Banani 11 Bridge", envelope.Data.RoadName);
        Assert.Equal(ClosureSeverity.TotalClosure, envelope.Data.Severity);
        Assert.Equal("Use Kemal Ataturk Avenue", envelope.Data.AlternateRouteAdvice);
    }

    [Fact]
    public async Task CreateRoadClosure_CitizenRole_ReturnsForbidden()
    {
        var client = Client(Roles.Citizen);
        var req = new CreateRoadClosureRequest(
            "Unauthorized Road Closure",
            "Citizens cannot close roads",
            ClosureSeverity.Caution,
            null,
            "Obstruction",
            null,
            null);

        var response = await client.PostAsJsonAsync(ClosuresPath, req);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateRoadClosure_GovernmentRole_UpdatesSuccessfully()
    {
        var client = Client(Roles.Government);
        var createReq = new CreateRoadClosureRequest(
            "Bijoy Sarani Overpass",
            "Water logging",
            ClosureSeverity.Caution,
            null,
            "1 foot water",
            "Use Airport Road",
            null);

        var createRes = await client.PostAsJsonAsync(ClosuresPath, createReq);
        var createdDto = (await createRes.Content.ReadFromJsonAsync<ApiEnvelope<RoadClosureDto>>())!.Data;

        var updateReq = new UpdateRoadClosureRequest(
            "Bijoy Sarani Overpass",
            "Water pumped out, lane reopened",
            ClosureSeverity.Caution,
            null,
            "Pumping complete",
            "Normal traffic flow",
            false,
            DateTimeOffset.UtcNow);

        var updateRes = await client.PutAsJsonAsync($"{ClosuresPath}/{createdDto.Id}", updateReq);
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);
        var updatedDto = (await updateRes.Content.ReadFromJsonAsync<ApiEnvelope<RoadClosureDto>>())!.Data;
        Assert.False(updatedDto.IsActive);
        Assert.Equal("Water pumped out, lane reopened", updatedDto.Description);
    }

    [Fact]
    public async Task DeleteRoadClosure_GovernmentRole_ReturnsNoContent()
    {
        var client = Client(Roles.Government);
        var createReq = new CreateRoadClosureRequest(
            "Road To Delete",
            "Temporary block",
            ClosureSeverity.Impasse,
            null,
            "Debris",
            null,
            null);

        var createRes = await client.PostAsJsonAsync(ClosuresPath, createReq);
        var createdDto = (await createRes.Content.ReadFromJsonAsync<ApiEnvelope<RoadClosureDto>>())!.Data;

        var deleteRes = await client.DeleteAsync($"{ClosuresPath}/{createdDto.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteRes.StatusCode);
    }

    [Fact]
    public async Task DegradedMode_Returns503ProblemDetails_WhenPostgresUnavailable()
    {
        var health = _factory.Services.GetRequiredService<DatabaseHealth>();
        var previousState = health.PostgresAvailable;
        try
        {
            health.PostgresAvailable = false;
            var client = Client(Roles.Government);
            var req = new CreateSafetyZoneRequest(
                "Degraded Zone",
                "Testing 503 fallback",
                ZoneType.DangerZone,
                Severity.Severe,
                "Circle",
                23.8103,
                90.4125,
                500,
                null,
                null);

            var response = await client.PostAsJsonAsync(ZonesPath, req);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }
        finally
        {
            health.PostgresAvailable = previousState;
        }
    }
}
