using System.Net;
using System.Text.Json;
using RapidRelief.Api.Features.Realtime.Hubs;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Api.Tests.Auth;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Tests.Realtime;

/// <summary>
/// Hub authentication: the negotiate handshake is the auth boundary. The ?access_token= query
/// hook in AuthSetup is only reachable this way — the .NET client sends a real Authorization
/// header, so nothing else exercises it.
/// </summary>
public sealed class HubAuthTests : IClassFixture<TestingWebAppFactory>
{
    private const string Negotiate = NotificationsHub.Path + "/negotiate?negotiateVersion=1";

    private readonly TestingWebAppFactory _factory;

    public HubAuthTests(TestingWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task Negotiate_without_credentials_is_401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync(Negotiate, content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Negotiate_with_a_dev_role_header_is_200()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(FakeAuthHandler.HeaderName, Roles.Citizen);

        var response = await client.PostAsync(Negotiate, content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("connectionId").GetString()));
    }

    [Fact]
    public async Task Negotiate_with_an_access_token_query_parameter_is_200()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);
        var (session, _, _) = await AuthTestClient.RegisterFreshUserAsync(client);

        var response = await client.PostAsync(
            $"{Negotiate}&access_token={Uri.EscapeDataString(session.AccessToken)}", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Negotiate_with_a_bogus_access_token_query_parameter_is_401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync($"{Negotiate}&access_token=not.a.jwt", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
