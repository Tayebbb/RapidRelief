using System.Net;
using System.Text.Json;

namespace RapidRelief.Api.Tests.Foundation;

/// <summary>
/// GET /api/foundation/demo-incidents — the foundation-owned demo surface feeding the RapidMap
/// proof on /sample (anonymous, stub-backed, remove-noted once F2/F7 own real incident endpoints).
/// </summary>
public sealed class DemoIncidentsEndpointTests : IClassFixture<TestingWebAppFactory>
{
    private readonly TestingWebAppFactory _factory;

    public DemoIncidentsEndpointTests(TestingWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Demo_incidents_returns_paged_envelope_with_all_seeded_incidents_anonymously()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/foundation/demo-incidents");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = body.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("totalCount").GetInt32() >= 25);
        Assert.True(data.GetProperty("items").GetArrayLength() >= 25);
        var first = data.GetProperty("items")[0];
        Assert.True(first.TryGetProperty("location", out var location));
        Assert.InRange(location.GetProperty("latitude").GetDouble(), 23.6, 24.0);
        Assert.InRange(location.GetProperty("longitude").GetDouble(), 90.2, 90.6);
    }
}
