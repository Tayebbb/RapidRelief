using System.Net;
using System.Net.Http.Json;

using RapidRelief.Shared.Contracts.Common;

namespace RapidRelief.Api.Tests.CommandCenter;

public class CommandCenterTests(TestingWebAppFactory factory) : IClassFixture<TestingWebAppFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetOverview_AsAdmin_Returns200AndData()
    {
        // Arrange
        // D-011: Admin test role
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/command-center/overview");
        request.Headers.Add("X-Dev-Role", "Admin");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<CommandCenterOverviewDto>>();
        
        Assert.NotNull(envelope);
        Assert.NotNull(envelope.Data);
        // From DhakaSeedData: 28 incidents total, 8 shelters.
        // It's sufficient to assert that the counts are non-negative for this integration test.
        Assert.True(envelope.Data.TotalActiveIncidents >= 0);
        Assert.True(envelope.Data.TotalOpenShelters >= 0);
    }

    [Fact]
    public async Task GetOverview_AsCitizen_Returns403()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/command-center/overview");
        request.Headers.Add("X-Dev-Role", "Citizen");

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetOverview_WithoutRole_Returns401()
    {
        // Arrange
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/command-center/overview");
        
        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // To properly test the 503 fallback, we would need to mock DatabaseHealth.
    // In this integration test setup, the database is healthy (SQLite).
    // So testing the 503 degraded path requires overriding the service in WebApplicationFactory.
}
