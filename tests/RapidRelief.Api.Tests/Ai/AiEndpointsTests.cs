using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Eventing;
using RapidRelief.Shared.Contracts.Events;

namespace RapidRelief.Api.Tests.Ai;

/// <summary>
/// F8 blueprint TEST PLAN item 7 — /api/ai surface: authz, envelope shapes, no-store header,
/// 404/400/503 gates, and D-027 recommendation determinism against the Dhaka seed data.
/// </summary>
public sealed class AiEndpointsTests : IClassFixture<TestingWebAppFactory>
{
    /// <summary>Seeded incident 1: Flood, Verified, Mirpur 10 (23.8210, 90.3665).</summary>
    private static readonly Guid SeededFloodIncident = Guid.Parse("a0000000-0000-0000-0000-000000000001");

    private static readonly TimeSpan PollDeadline = TimeSpan.FromSeconds(15);

    private readonly TestingWebAppFactory _factory;

    public AiEndpointsTests(TestingWebAppFactory factory) => _factory = factory;

    private HttpClient CreateClientWithRole(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(FakeAuthHandler.HeaderName, role);
        return client;
    }

    private async Task PublishAsync(IncidentCreated evt)
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IEventBus>().PublishAsync(evt);
    }

    private static IncidentCreated Evt(Guid incidentId, double lat, double lon)
        => new(incidentId, Guid.NewGuid(), DisasterType.Flood, Severity.Moderate,
            new GeoPoint(lat, lon), "water rising in the street", false, Array.Empty<string>());

    private async Task<HttpResponseMessage> WaitFor200Async(HttpClient client, string url)
    {
        var stopAt = DateTime.UtcNow + PollDeadline;
        HttpResponseMessage response;
        do
        {
            response = await client.GetAsync(url);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return response;
            }
            await Task.Delay(50);
        }
        while (DateTime.UtcNow < stopAt);
        return response;
    }

    [Theory]
    [InlineData("/api/ai/assessments/a0000000-0000-0000-0000-000000000001")]
    [InlineData("/api/ai/recommendations/shelter?incidentId=a0000000-0000-0000-0000-000000000001")]
    [InlineData("/api/ai/recommendations/team?incidentId=a0000000-0000-0000-0000-000000000001")]
    [InlineData("/api/ai/recommendations/resource?incidentId=a0000000-0000-0000-0000-000000000001")]
    public async Task Unauthenticated_requests_are_rejected_with_401(string url)
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// An incident id is not a secret. These routes take one and return the AI summary derived
    /// from the reporter's free text, so a citizen holding someone else's id must be refused —
    /// they read their own AI estimate through the owner-scoped incident DTO instead.
    /// </summary>
    [Theory]
    [InlineData("/api/ai/assessments/a0000000-0000-0000-0000-000000000001")]
    [InlineData("/api/ai/insights/a0000000-0000-0000-0000-000000000001")]
    [InlineData("/api/ai/recommendations/shelter?incidentId=a0000000-0000-0000-0000-000000000001")]
    [InlineData("/api/ai/recommendations/team?incidentId=a0000000-0000-0000-0000-000000000001")]
    [InlineData("/api/ai/recommendations/resource?incidentId=a0000000-0000-0000-0000-000000000001")]
    public async Task A_citizen_cannot_read_decision_support_for_an_arbitrary_incident(string url)
    {
        var client = CreateClientWithRole(Roles.Citizen);

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_incident_assessment_returns_404_problem()
    {
        var client = CreateClientWithRole(Roles.Rescuer);

        var response = await client.GetAsync($"/api/ai/assessments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Assessment_endpoint_returns_envelope_with_exact_shape_and_no_store_header()
    {
        var incidentId = Guid.NewGuid();
        await PublishAsync(Evt(incidentId, 23.7461, 90.3742));
        var client = CreateClientWithRole(Roles.Rescuer);

        var response = await WaitFor200Async(client, $"/api/ai/assessments/{incidentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store, private", response.Headers.CacheControl?.ToString());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = body.RootElement.GetProperty("data");
        Assert.Equal(incidentId, data.GetProperty("incidentId").GetGuid());
        Assert.Equal((int)DisasterType.Flood, data.GetProperty("predictedType").GetInt32());
        Assert.Equal((int)Severity.Moderate, data.GetProperty("estimatedSeverity").GetInt32());
        Assert.InRange(data.GetProperty("priorityScore").GetDouble(), 0, 100);
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("summary").GetString()));
        Assert.Equal(JsonValueKind.Null, data.GetProperty("possibleDuplicateOfId").ValueKind);
        Assert.Equal("RuleBased", data.GetProperty("provider").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("modelName").ValueKind);
        Assert.True(data.GetProperty("latencyMs").GetInt32() >= 0);
        Assert.True(data.TryGetProperty("createdAtUtc", out _));
    }

    [Fact]
    public async Task Assessment_endpoint_returns_503_problem_when_database_is_degraded()
    {
        var health = _factory.Services.GetRequiredService<DatabaseHealth>();
        var client = CreateClientWithRole(Roles.Admin);
        try
        {
            health.PostgresAvailable = false;

            var response = await client.GetAsync($"/api/ai/assessments/{Guid.NewGuid()}");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
        finally
        {
            health.PostgresAvailable = true;
        }
    }

    [Fact]
    public async Task Missing_incident_id_returns_400()
    {
        var client = CreateClientWithRole(Roles.Rescuer);

        var response = await client.GetAsync("/api/ai/recommendations/shelter");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_incident_recommendations_return_404()
    {
        var client = CreateClientWithRole(Roles.Rescuer);

        var response = await client.GetAsync($"/api/ai/recommendations/shelter?incidentId={Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Shelter_recommendations_are_deterministic_and_exclude_full_and_closed()
    {
        var client = CreateClientWithRole(Roles.Rescue);

        var response = await client.GetAsync($"/api/ai/recommendations/shelter?incidentId={SeededFloodIncident}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = body.RootElement.GetProperty("data");
        Assert.Equal(SeededFloodIncident, data.GetProperty("incidentId").GetGuid());
        Assert.Equal("Shelter", data.GetProperty("kind").GetString());
        Assert.Equal("ShelterReadService", data.GetProperty("sourcedFrom").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("reason").ValueKind);

        var candidates = data.GetProperty("candidates").EnumerateArray().ToList();
        Assert.Equal(3, candidates.Count);
        // Nearest open shelters with free capacity to Mirpur 10: Mirpur High School,
        // Dhanmondi College, Khilgaon Colony School — never the full (…02) or closed (…05) ones.
        Assert.Equal(Guid.Parse("b0000000-0000-0000-0000-000000000001"), candidates[0].GetProperty("id").GetGuid());
        Assert.Equal(Guid.Parse("b0000000-0000-0000-0000-000000000003"), candidates[1].GetProperty("id").GetGuid());
        Assert.Equal(Guid.Parse("b0000000-0000-0000-0000-000000000006"), candidates[2].GetProperty("id").GetGuid());
        Assert.Equal("free capacity 280", candidates[0].GetProperty("detail").GetString());
        Assert.True(candidates[0].GetProperty("distanceKm").GetDouble() > 0);
        Assert.True(candidates[0].GetProperty("distanceKm").GetDouble()
            < candidates[1].GetProperty("distanceKm").GetDouble());
    }

    [Fact]
    public async Task Team_recommendations_match_flood_skills_from_the_volunteer_registry()
    {
        var client = CreateClientWithRole(Roles.Rescue);

        var response = await client.GetAsync($"/api/ai/recommendations/team?incidentId={SeededFloodIncident}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = body.RootElement.GetProperty("data");
        Assert.Equal("Team", data.GetProperty("kind").GetString());
        Assert.Equal("VolunteerRegistry", data.GetProperty("sourcedFrom").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("reason").ValueKind);

        var candidates = data.GetProperty("candidates").EnumerateArray().ToList();
        // Flood → Swimming/Boating: available with location = Arif (Swimming, ~4.9 km)
        // then Mehedi (Boating+Swimming, ~12.3 km). Rakib has RopeWork and is unavailable.
        Assert.Equal(2, candidates.Count);
        Assert.Equal(Guid.Parse("d0000000-0000-0000-0000-000000000001"), candidates[0].GetProperty("id").GetGuid());
        Assert.Equal("Swimming", candidates[0].GetProperty("detail").GetString());
        Assert.Equal(Guid.Parse("d0000000-0000-0000-0000-000000000007"), candidates[1].GetProperty("id").GetGuid());
        Assert.Equal("Boating, Swimming", candidates[1].GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Resource_recommendations_match_flood_focus_areas_from_the_ngo_registry()
    {
        var client = CreateClientWithRole(Roles.Ngo);

        var response = await client.GetAsync($"/api/ai/recommendations/resource?incidentId={SeededFloodIncident}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = body.RootElement.GetProperty("data");
        Assert.Equal("Resource", data.GetProperty("kind").GetString());
        Assert.Equal("NgoRegistry", data.GetProperty("sourcedFrom").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("reason").ValueKind);

        var candidates = data.GetProperty("candidates").EnumerateArray().ToList();
        // Flood → "Flood Relief"/"Food": BRAC (Flood Relief) then Bidyanondo (Food), seed order.
        Assert.Equal(2, candidates.Count);
        Assert.Equal(Guid.Parse("e0000000-0000-0000-0000-000000000001"), candidates[0].GetProperty("id").GetGuid());
        Assert.Equal("Flood Relief", candidates[0].GetProperty("detail").GetString());
        Assert.Equal(JsonValueKind.Null, candidates[0].GetProperty("distanceKm").ValueKind);
        Assert.Equal(Guid.Parse("e0000000-0000-0000-0000-000000000002"), candidates[1].GetProperty("id").GetGuid());
        Assert.Equal("Food", candidates[1].GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Pipeline_created_incident_resolves_recommendations_from_its_snapshot_row()
    {
        // Not in the seed data — only the ai_assessments snapshot knows this incident.
        var incidentId = Guid.NewGuid();
        await PublishAsync(Evt(incidentId, 23.8210, 90.3665));
        var client = CreateClientWithRole(Roles.Rescuer);
        Assert.Equal(HttpStatusCode.OK,
            (await WaitFor200Async(client, $"/api/ai/assessments/{incidentId}")).StatusCode);

        var response = await client.GetAsync($"/api/ai/recommendations/shelter?incidentId={incidentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var candidates = body.RootElement.GetProperty("data").GetProperty("candidates").EnumerateArray().ToList();
        Assert.Equal(3, candidates.Count);
        // Same origin as seeded incident 1 → same deterministic top-3.
        Assert.Equal(Guid.Parse("b0000000-0000-0000-0000-000000000001"), candidates[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Recommendation_responses_carry_the_no_store_header()
    {
        var client = CreateClientWithRole(Roles.Rescuer);

        var response = await client.GetAsync($"/api/ai/recommendations/resource?incidentId={SeededFloodIncident}");

        Assert.Equal("no-store, private", response.Headers.CacheControl?.ToString());
    }
}
