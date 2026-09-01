using System.Net;

namespace RapidRelief.Api.Tests.Foundation;

public sealed class ApiFallbackTests : IClassFixture<TestingWebAppFactory>
{
    private readonly TestingWebAppFactory _factory;

    public ApiFallbackTests(TestingWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Unknown_api_route_returns_404_problem_json_not_spa_html()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/nonexistent");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Not found", body.RootElement.GetProperty("title").GetString());
    }
}
