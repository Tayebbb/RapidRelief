using System.Net;

namespace RapidRelief.Api.Tests.Auth;

/// <summary>Blueprint TEST PLAN ⑰.</summary>
public sealed class LogoutTests : IClassFixture<TestingWebAppFactory>
{
    private readonly TestingWebAppFactory _factory;

    public LogoutTests(TestingWebAppFactory factory) => _factory = factory;

    [Fact] // ⑰
    public async Task Logout_returns_204_deletes_cookie_and_revokes_the_row_server_side()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);
        var (session, cookie, _) = await AuthTestClient.RegisterFreshUserAsync(client);

        var logout = await AuthTestClient.LogoutAsync(client, session.AccessToken, cookie);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        var deleteHeader = AuthTestClient.FindSetCookieHeader(logout);
        Assert.NotNull(deleteHeader);
        Assert.StartsWith($"{AuthTestClient.CookieName}=;", deleteHeader); // emptied value = delete

        // Server-side revocation, not just client state: the old cookie must be dead.
        var refresh = await AuthTestClient.RefreshAsync(client, cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact] // ⑰ idempotency
    public async Task Repeated_logout_still_returns_204()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);
        var (session, cookie, _) = await AuthTestClient.RegisterFreshUserAsync(client);

        var first = await AuthTestClient.LogoutAsync(client, session.AccessToken, cookie);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var second = await AuthTestClient.LogoutAsync(client, session.AccessToken, cookie);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

        var withoutCookie = await AuthTestClient.LogoutAsync(client, session.AccessToken, rawCookie: null);
        Assert.Equal(HttpStatusCode.NoContent, withoutCookie.StatusCode);
    }

    [Fact]
    public async Task Logout_without_bearer_returns_401()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);

        var response = await client.PostAsync("/api/auth/logout", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
