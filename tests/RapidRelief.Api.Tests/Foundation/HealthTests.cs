using System.Net;
using System.Text.Json;

namespace RapidRelief.Api.Tests.Foundation;

/// <summary>
/// /health degraded-mode reporting (D-005). In Testing the factory's EnsureCreated succeeded,
/// so the endpoint must report status "ok" with dbConnected=true.
/// </summary>
public sealed class HealthTests : IClassFixture<TestingWebAppFactory>
{
    private readonly TestingWebAppFactory _factory;

    public HealthTests(TestingWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Health_in_testing_env_reports_ok_with_db_connected_true()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ok", body.RootElement.GetProperty("status").GetString());
        Assert.True(body.RootElement.GetProperty("dbConnected").GetBoolean());
    }
}
