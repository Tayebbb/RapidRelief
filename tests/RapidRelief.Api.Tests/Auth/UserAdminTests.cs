using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RapidRelief.Api.Features.Auth.Services;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Tests.Auth;

/// <summary>Blueprint TEST PLAN ㉘–㉞.</summary>
public sealed class UserAdminTests : IClassFixture<TestingWebAppFactory>
{
    private readonly TestingWebAppFactory _factory;

    public UserAdminTests(TestingWebAppFactory factory) => _factory = factory;

    private async Task<AuthSession> LoginAdminAsync(HttpClient client)
    {
        var response = await AuthTestClient.LoginAsync(client, "admin1@rr.dev");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await AuthTestClient.ReadSessionAsync(response);
    }

    [Fact] // ㉘ — real-login Admin sees the seeded users; paging clamps hold
    public async Task Get_users_as_real_admin_returns_paged_envelope_with_seeded_users()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);
        var admin = await LoginAdminAsync(client);

        var response = await client.SendAsync(
            AuthTestClient.BearerRequest(HttpMethod.Get, "/api/auth/users?page=0&pageSize=99999", admin.AccessToken));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = body.RootElement.GetProperty("data");
        Assert.Equal(1, data.GetProperty("page").GetInt32());        // clamped from 0
        Assert.Equal(200, data.GetProperty("pageSize").GetInt32());  // clamped from 99999
        Assert.True(data.GetProperty("totalCount").GetInt32() >= 4);

        var items = data.GetProperty("items").EnumerateArray().ToList();
        foreach (var role in Roles.All)
        {
            var seeded = items.SingleOrDefault(i =>
                i.GetProperty("email").GetString() == $"{role.ToLowerInvariant()}1@rr.dev");
            Assert.True(seeded.ValueKind == JsonValueKind.Object, $"seeded {role} user missing from page");
            Assert.Contains(role, seeded.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
            Assert.False(seeded.GetProperty("isLocked").GetBoolean());
        }

        var overflow = await client.SendAsync(
            AuthTestClient.BearerRequest(HttpMethod.Get, $"/api/auth/users?page={int.MaxValue}&pageSize=50", admin.AccessToken));
        Assert.Equal(HttpStatusCode.OK, overflow.StatusCode); // clamp prevents overflow 500
        using var overflowBody = JsonDocument.Parse(await overflow.Content.ReadAsStringAsync());
        Assert.Empty(overflowBody.RootElement.GetProperty("data").GetProperty("items").EnumerateArray());
    }

    [Fact] // ㉙ — policy pins
    public async Task Get_users_as_citizen_returns_403_and_anonymous_401()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);
        var citizenLogin = await AuthTestClient.LoginAsync(client, "citizen1@rr.dev");
        Assert.Equal(HttpStatusCode.OK, citizenLogin.StatusCode);
        var citizen = await AuthTestClient.ReadSessionAsync(citizenLogin);

        var forbidden = await client.SendAsync(
            AuthTestClient.BearerRequest(HttpMethod.Get, "/api/auth/users", citizen.AccessToken));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var anonymous = await client.GetAsync("/api/auth/users");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact] // ㉚ — lock → refresh 401 AND login 401; unlock → login works
    public async Task Lock_kills_refresh_and_login_until_unlock()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);
        var admin = await LoginAdminAsync(client);
        var (victim, victimCookie, victimEmail) = await AuthTestClient.RegisterFreshUserAsync(client);

        var lockResponse = await SendLockAsync(client, admin.AccessToken, victim.UserId, locked: true);
        Assert.Equal(HttpStatusCode.NoContent, lockResponse.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await AuthTestClient.RefreshAsync(client, victimCookie)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await AuthTestClient.LoginAsync(client, victimEmail)).StatusCode);

        var unlockResponse = await SendLockAsync(client, admin.AccessToken, victim.UserId, locked: false);
        Assert.Equal(HttpStatusCode.NoContent, unlockResponse.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await AuthTestClient.LoginAsync(client, victimEmail)).StatusCode);
    }

    [Fact] // ㉛ — role change: old refresh dead; fresh login carries the new role only
    public async Task Role_change_revokes_old_refresh_and_new_login_carries_new_roles()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);
        var admin = await LoginAdminAsync(client);
        var (victim, victimCookie, victimEmail) = await AuthTestClient.RegisterFreshUserAsync(client);

        var setRoles = await SendRolesAsync(client, admin.AccessToken, victim.UserId, [Roles.Ngo]);
        Assert.Equal(HttpStatusCode.NoContent, setRoles.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await AuthTestClient.RefreshAsync(client, victimCookie)).StatusCode);

        var relogin = await AuthTestClient.LoginAsync(client, victimEmail);
        Assert.Equal(HttpStatusCode.OK, relogin.StatusCode);
        var session = await AuthTestClient.ReadSessionAsync(relogin);
        Assert.Equal([Roles.Ngo], session.Roles);
    }

    [Fact] // ㉜ — unknown role 400 via validator; case-sensitivity pin; service defense-in-depth pin
    public async Task Unknown_or_wrong_case_roles_return_400_and_service_returns_false()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);
        var admin = await LoginAdminAsync(client);
        var (victim, _, _) = await AuthTestClient.RegisterFreshUserAsync(client);

        var hacker = await SendRolesAsync(client, admin.AccessToken, victim.UserId, ["Hacker"]);
        Assert.Equal(HttpStatusCode.BadRequest, hacker.StatusCode);

        var wrongCase = await SendRolesAsync(client, admin.AccessToken, victim.UserId, ["Ngo"]); // must be "NGO"
        Assert.Equal(HttpStatusCode.BadRequest, wrongCase.StatusCode);

        var emptyList = await SendRolesAsync(client, admin.AccessToken, victim.UserId, []);
        Assert.Equal(HttpStatusCode.BadRequest, emptyList.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IUserAdminService>();
        Assert.False(await service.SetRolesAsync(victim.UserId, ["Hacker"])); // security backstop (risk 7)
    }

    [Fact] // ㉝ — unknown ids
    public async Task Lock_and_roles_on_unknown_guid_return_404()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);
        var admin = await LoginAdminAsync(client);
        var unknown = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await SendLockAsync(client, admin.AccessToken, unknown, true)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SendRolesAsync(client, admin.AccessToken, unknown, [Roles.Ngo])).StatusCode);
    }

    [Fact] // self-guard (blueprint §6 rows 10–11)
    public async Task Admin_cannot_lock_or_rerole_own_account()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);
        var admin = await LoginAdminAsync(client);

        Assert.Equal(HttpStatusCode.BadRequest, (await SendLockAsync(client, admin.AccessToken, admin.UserId, true)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SendRolesAsync(client, admin.AccessToken, admin.UserId, [Roles.Citizen])).StatusCode);
    }

    [Fact] // ㉞ — DI displacement pin (stub-yield proven)
    public void UserAdminService_resolves_to_identity_implementation_not_the_fake()
    {
        using var scope = _factory.Services.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<IUserAdminService>();

        Assert.IsType<IdentityUserAdminService>(service);
    }

    private static Task<HttpResponseMessage> SendLockAsync(HttpClient client, string accessToken, Guid id, bool locked)
    {
        var request = AuthTestClient.BearerRequest(HttpMethod.Post, $"/api/auth/users/{id}/lock", accessToken);
        request.Content = JsonContent.Create(new { locked });
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SendRolesAsync(HttpClient client, string accessToken, Guid id, string[] roles)
    {
        var request = AuthTestClient.BearerRequest(HttpMethod.Put, $"/api/auth/users/{id}/roles", accessToken);
        request.Content = JsonContent.Create(new { roles });
        return client.SendAsync(request);
    }
}
