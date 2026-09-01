using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Tests.Auth;

/// <summary>Blueprint TEST PLAN ①–⑤.</summary>
public sealed class RegisterTests : IClassFixture<TestingWebAppFactory>
{
    private readonly TestingWebAppFactory _factory;

    public RegisterTests(TestingWebAppFactory factory) => _factory = factory;

    [Fact] // ①
    public async Task Register_happy_returns_201_envelope_citizen_role_and_refresh_cookie_with_exact_attributes()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);
        var email = AuthTestClient.UniqueEmail();

        var response = await AuthTestClient.RegisterAsync(client, email, displayName: "Fresh Citizen");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/auth/profile", response.Headers.Location?.ToString());

        var session = await AuthTestClient.ReadSessionAsync(response);
        Assert.NotEqual(Guid.Empty, session.UserId);
        Assert.Equal(email, session.Email);
        Assert.Equal([Roles.Citizen], session.Roles);
        Assert.False(string.IsNullOrWhiteSpace(session.AccessToken));

        var setCookie = AuthTestClient.FindSetCookieHeader(response);
        Assert.NotNull(setCookie);
        var lower = setCookie!.ToLowerInvariant();
        Assert.Contains("httponly", lower);
        Assert.Contains("samesite=strict", lower);
        Assert.Contains("path=/api/auth", lower);
        Assert.DoesNotContain("secure", lower); // Testing joins the D-012 Secure gate
    }

    [Fact] // ② — D-016 pin: a smuggled role field must be ignored
    public async Task Register_with_smuggled_role_json_still_creates_citizen_only()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);
        var email = AuthTestClient.UniqueEmail();

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = AuthTestClient.DemoPassword,
            displayName = "Wannabe Admin",
            roles = new[] { Roles.Admin },
            role = Roles.Admin,
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var session = await AuthTestClient.ReadSessionAsync(response);
        Assert.Equal([Roles.Citizen], session.Roles);
    }

    [Fact] // ③
    public async Task Register_duplicate_email_returns_400_keyed_by_identity_duplicate_code()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);
        var email = AuthTestClient.UniqueEmail();
        var first = await AuthTestClient.RegisterAsync(client, email);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await AuthTestClient.RegisterAsync(client, email);

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        using var body = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        var errorKeys = body.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains(errorKeys, key => key.StartsWith("Duplicate", StringComparison.Ordinal));
    }

    [Fact] // ④a — IdentityResult mapping pin
    public async Task Register_weak_password_returns_400_with_identity_password_codes()
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);

        var response = await AuthTestClient.RegisterAsync(client, AuthTestClient.UniqueEmail(), password: "alllowercase");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errorKeys = body.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains(errorKeys, key => key.StartsWith("Password", StringComparison.Ordinal));
    }

    [Theory] // ④b/⑤ — FluentValidation shape errors
    [InlineData("", "Valid Name", "Email")]
    [InlineData("not-an-email", "Valid Name", "Email")]
    [InlineData("valid@rr.dev", "", "DisplayName")]
    public async Task Register_invalid_shape_returns_400_with_fluent_validation_key(string email, string displayName, string expectedKey)
    {
        var client = AuthTestClient.CreateNoCookieClient(_factory);

        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = AuthTestClient.DemoPassword,
            displayName,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty(expectedKey, out _));
    }
}
