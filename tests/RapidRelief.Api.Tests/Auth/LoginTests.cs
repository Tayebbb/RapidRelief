using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Tests.Auth;

/// <summary>Blueprint TEST PLAN ⑥–⑨ + access-token negatives ⑱–⑲.</summary>
public sealed class LoginTests : IClassFixture<TestingWebAppFactory>
{
    private readonly TestingWebAppFactory _factory;

    public LoginTests(TestingWebAppFactory factory) => _factory = factory;

    [Fact] // ⑥ — seeded login + R9 claims-parity pin vs FakeAuth
    public async Task Login_seeded_citizen_succeeds_and_bearer_whoami_matches_fake_auth_principal()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);

        var login = await AuthTestClient.LoginAsync(client, "citizen1@rr.dev");

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.NotNull(AuthTestClient.ExtractRefreshCookie(login));
        var session = await AuthTestClient.ReadSessionAsync(login);

        var bearerWhoami = await client.SendAsync(
            AuthTestClient.BearerRequest(HttpMethod.Get, "/api/foundation/whoami", session.AccessToken));
        Assert.Equal(HttpStatusCode.OK, bearerWhoami.StatusCode);
        var viaJwt = await ReadWhoAmIAsync(bearerWhoami);

        var fakeRequest = new HttpRequestMessage(HttpMethod.Get, "/api/foundation/whoami");
        fakeRequest.Headers.Add(FakeAuthHandler.HeaderName, Roles.Citizen);
        var fakeWhoami = await client.SendAsync(fakeRequest);
        Assert.Equal(HttpStatusCode.OK, fakeWhoami.StatusCode);
        var viaFake = await ReadWhoAmIAsync(fakeWhoami);

        Assert.Equal(viaFake, viaJwt); // identical (Name, Id, Roles) triple
        Assert.Equal(FakeAuthHandler.SeedUserIds[Roles.Citizen].ToString(), viaJwt.Id);
        Assert.Equal("citizen1@rr.dev", viaJwt.Name);
        Assert.Equal(Roles.Citizen, Assert.Single(viaJwt.Roles.Split('|')));
    }

    [Fact] // ⑦ — enumeration uniformity: byte-identical 401 bodies
    public async Task Login_unknown_email_wrong_password_and_locked_user_return_byte_identical_401_bodies()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);

        // Fresh locked victim (never lock the shared seeded users).
        var (victim, _, victimEmail) = await AuthTestClient.RegisterFreshUserAsync(client);
        var adminLock = new HttpRequestMessage(HttpMethod.Post, $"/api/auth/users/{victim.UserId}/lock")
        {
            Content = JsonContent(new { locked = true }),
        };
        adminLock.Headers.Add(FakeAuthHandler.HeaderName, Roles.Admin);
        var lockResponse = await client.SendAsync(adminLock);
        Assert.Equal(HttpStatusCode.NoContent, lockResponse.StatusCode);

        var unknownEmail = await AuthTestClient.LoginAsync(client, "nobody-here@rr.dev");
        var wrongPassword = await AuthTestClient.LoginAsync(client, "citizen1@rr.dev", "Wrong!123");
        var lockedUser = await AuthTestClient.LoginAsync(client, victimEmail);

        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, lockedUser.StatusCode);

        var bodyA = await unknownEmail.Content.ReadAsStringAsync();
        var bodyB = await wrongPassword.Content.ReadAsStringAsync();
        var bodyC = await lockedUser.Content.ReadAsStringAsync();
        Assert.Equal(bodyA, bodyB);
        Assert.Equal(bodyB, bodyC);
    }

    [Fact] // ⑧ — lockout accounting stays uniform
    public async Task Login_sixth_attempt_with_correct_password_after_five_failures_returns_uniform_401()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);
        var (_, _, email) = await AuthTestClient.RegisterFreshUserAsync(client);

        for (var i = 0; i < 5; i++)
        {
            var failed = await AuthTestClient.LoginAsync(client, email, "Wrong!123");
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        var sixthCorrect = await AuthTestClient.LoginAsync(client, email);

        Assert.Equal(HttpStatusCode.Unauthorized, sixthCorrect.StatusCode);
        using var body = JsonDocument.Parse(await sixthCorrect.Content.ReadAsStringAsync());
        Assert.Equal("Invalid credentials", body.RootElement.GetProperty("title").GetString());
    }

    [Fact] // ⑨ — login shape errors are FluentValidation 400s, not 401s
    public async Task Login_empty_fields_return_400_validation_problem()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);

        var response = await AuthTestClient.LoginAsync(client, "", "");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.TryGetProperty("errors", out _));
    }

    [Fact] // ⑱ — expired token (beyond the 1-min ClockSkew, D-013)
    public async Task Expired_hand_minted_token_is_rejected_with_401()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);
        var token = MintToken(TestingWebAppFactory.TestSigningKey, expiresUtc: DateTime.UtcNow.AddMinutes(-2));

        var response = await client.SendAsync(
            AuthTestClient.BearerRequest(HttpMethod.Get, "/api/auth/profile", token));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact] // ⑲ — wrong signing key
    public async Task Token_signed_with_different_key_is_rejected_with_401()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);
        var token = MintToken(new string('x', 64), expiresUtc: DateTime.UtcNow.AddMinutes(30));

        var response = await client.SendAsync(
            AuthTestClient.BearerRequest(HttpMethod.Get, "/api/auth/profile", token));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static string MintToken(string signingKey, DateTime expiresUtc)
    {
        var handler = new JwtSecurityTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, FakeAuthHandler.SeedUserIds[Roles.Citizen].ToString()),
                new Claim(ClaimTypes.Name, "citizen1@rr.dev"),
                new Claim(ClaimTypes.Role, Roles.Citizen),
            ]),
            Issuer = "RapidRelief",
            Audience = "RapidRelief",
            NotBefore = expiresUtc.AddMinutes(-30),
            Expires = expiresUtc,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256),
        };
        return handler.WriteToken(handler.CreateJwtSecurityToken(descriptor));
    }

    private static async Task<(string Name, string Id, string Roles)> ReadWhoAmIAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var data = body.RootElement.GetProperty("data");
        return (
            data.GetProperty("name").GetString()!,
            data.GetProperty("id").GetString()!,
            string.Join('|', data.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).OrderBy(r => r)));
    }

    private static HttpContent JsonContent(object payload) =>
        new StringContent(System.Text.Json.JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
}
