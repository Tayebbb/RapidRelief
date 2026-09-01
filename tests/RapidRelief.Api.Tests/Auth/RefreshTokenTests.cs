using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RapidRelief.Api.Features.Auth.Data;
using RapidRelief.Api.Features.Auth.Domain;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Tests.Auth;

/// <summary>Blueprint TEST PLAN ⑩–⑯.</summary>
public sealed class RefreshTokenTests : IClassFixture<TestingWebAppFactory>
{
    private readonly TestingWebAppFactory _factory;

    public RefreshTokenTests(TestingWebAppFactory factory) => _factory = factory;

    [Fact] // ⑩ — rotation happy path
    public async Task Refresh_returns_200_new_access_token_and_rotated_cookie()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);
        var (session, cookie, _) = await AuthTestClient.RegisterFreshUserAsync(client);

        var refresh = await AuthTestClient.RefreshAsync(client, cookie);

        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var rotated = AuthTestClient.ExtractRefreshCookie(refresh);
        Assert.NotNull(rotated);
        Assert.NotEqual(cookie, rotated);

        var newSession = await AuthTestClient.ReadSessionAsync(refresh);
        var whoami = await client.SendAsync(
            AuthTestClient.BearerRequest(HttpMethod.Get, "/api/foundation/whoami", newSession.AccessToken));
        Assert.Equal(HttpStatusCode.OK, whoami.StatusCode);
        Assert.Equal(session.UserId, newSession.UserId);
    }

    [Fact] // ⑪ — reuse detection revokes the whole family
    public async Task Replaying_pre_rotation_cookie_returns_401_and_kills_the_rotated_cookie_too()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);
        var (_, cookie1, _) = await AuthTestClient.RegisterFreshUserAsync(client);

        var rotate = await AuthTestClient.RefreshAsync(client, cookie1);
        Assert.Equal(HttpStatusCode.OK, rotate.StatusCode);
        var cookie2 = AuthTestClient.ExtractRefreshCookie(rotate)!;

        var replay = await AuthTestClient.RefreshAsync(client, cookie1);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        var familyDead = await AuthTestClient.RefreshAsync(client, cookie2);
        Assert.Equal(HttpStatusCode.Unauthorized, familyDead.StatusCode);
    }

    [Fact] // ⑫ — garbage cookie: uniform 401 + cookie delete
    public async Task Garbage_cookie_returns_401_and_deletes_the_cookie()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);

        var response = await AuthTestClient.RefreshAsync(client, "garbage-token-value");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var deleteHeader = AuthTestClient.FindSetCookieHeader(response);
        Assert.NotNull(deleteHeader); // expired Set-Cookie = delete instruction
        Assert.Contains("path=/api/auth", deleteHeader!.ToLowerInvariant());
    }

    [Fact] // ⑬ — no cookie at all
    public async Task Missing_cookie_returns_uniform_401()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);

        var response = await AuthTestClient.RefreshAsync(client, null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact] // ⑭ — security-stamp mismatch (role change bumps the stamp)
    public async Task Refresh_after_admin_role_change_returns_401_for_pre_change_cookie()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);
        var (session, cookie, _) = await AuthTestClient.RegisterFreshUserAsync(client);

        var setRoles = new HttpRequestMessage(HttpMethod.Put, $"/api/auth/users/{session.UserId}/roles")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { roles = new[] { Roles.Rescue } }),
        };
        setRoles.Headers.Add(FakeAuthHandler.HeaderName, Roles.Admin);
        var roleResponse = await client.SendAsync(setRoles);
        Assert.Equal(HttpStatusCode.NoContent, roleResponse.StatusCode);

        var refresh = await AuthTestClient.RefreshAsync(client, cookie);

        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact] // ⑮ — lock invalidates refresh immediately
    public async Task Refresh_after_admin_lock_returns_401()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);
        var (session, cookie, _) = await AuthTestClient.RegisterFreshUserAsync(client);

        var lockRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/auth/users/{session.UserId}/lock")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { locked = true }),
        };
        lockRequest.Headers.Add(FakeAuthHandler.HeaderName, Roles.Admin);
        var lockResponse = await client.SendAsync(lockRequest);
        Assert.Equal(HttpStatusCode.NoContent, lockResponse.StatusCode);

        var refresh = await AuthTestClient.RefreshAsync(client, cookie);

        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact] // ⑯ — D-013 absolute-expiry inheritance across two rotations
    public async Task All_rows_of_a_rotated_family_share_the_original_absolute_expiry()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);
        var (session, cookie1, _) = await AuthTestClient.RegisterFreshUserAsync(client);

        var rotate1 = await AuthTestClient.RefreshAsync(client, cookie1);
        Assert.Equal(HttpStatusCode.OK, rotate1.StatusCode);
        var cookie2 = AuthTestClient.ExtractRefreshCookie(rotate1)!;
        var rotate2 = await AuthTestClient.RefreshAsync(client, cookie2);
        Assert.Equal(HttpStatusCode.OK, rotate2.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        List<RefreshToken> rows = await db.RefreshTokens
            .Where(t => t.UserId == session.UserId)
            .ToListAsync();

        Assert.Equal(3, rows.Count);
        Assert.Single(rows, r => r.RevokedAtUtc == null); // only the newest heir is active
        var expiries = rows.Select(r => r.ExpiresAtUtc).Distinct().ToList();
        Assert.Single(expiries);

        var original = rows.OrderBy(r => r.CreatedAtUtc).First();
        Assert.NotNull(original.RevokedAtUtc);
        Assert.NotNull(original.ReplacedByTokenHash); // ⑧-style rotation audit chain
    }
}
