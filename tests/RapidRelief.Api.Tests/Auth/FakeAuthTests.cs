using System.Net;
using System.Text.Json;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Tests.Auth;

public sealed class FakeAuthTests : IClassFixture<TestingWebAppFactory>
{
    private readonly TestingWebAppFactory _factory;

    public FakeAuthTests(TestingWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task WhoAmI_returns_401_when_no_dev_role_header_present()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/foundation/whoami");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WhoAmI_returns_200_with_admin_identity_when_admin_dev_role_header_present()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(FakeAuthHandler.HeaderName, Roles.Admin);

        var response = await client.GetAsync("/api/foundation/whoami");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = body.RootElement.GetProperty("data");
        Assert.Equal("admin1@rr.dev", data.GetProperty("name").GetString());
        Assert.Equal(FakeAuthHandler.SeedUserIds[Roles.Admin].ToString(), data.GetProperty("id").GetString());
        Assert.Contains(Roles.Admin, data.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task WhoAmI_returns_401_when_dev_role_header_has_bogus_role()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(FakeAuthHandler.HeaderName, "Superman");

        var response = await client.GetAsync("/api/foundation/whoami");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WhoAmI_returns_401_when_dev_role_header_value_is_empty()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation(FakeAuthHandler.HeaderName, "");

        var response = await client.GetAsync("/api/foundation/whoami");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WhoAmI_returns_401_via_jwt_bearer_challenge_for_garbage_bearer_token()
    {
        // No X-Dev-Role header ⇒ MultiAuth must forward to JwtBearer even in Testing.
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer garbage");

        var response = await client.GetAsync("/api/foundation/whoami");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");
    }

    [Fact]
    public async Task Health_returns_200_ok_payload_anonymously()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ok", body.RootElement.GetProperty("status").GetString());
    }
}
