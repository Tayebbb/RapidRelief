using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace RapidRelief.Api.Tests.Auth;

/// <summary>
/// Cookie-precise helpers for the F1 auth flows: clients are created with HandleCookies=false
/// so tests fully control replay/rotation of the rr_refresh cookie.
/// </summary>
internal static class AuthTestClient
{
    public const string CookieName = "rr_refresh";
    public const string DemoPassword = "Demo!123";

    public static HttpClient CreateNoCookieClient(TestingWebAppFactory factory) =>
        factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
        });

    public static string UniqueEmail() => $"u{Guid.NewGuid():N}@rr.dev";

    public static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string email, string password = DemoPassword,
        string displayName = "Test User") =>
        client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password,
            displayName,
            phoneNumber = "01700000000",
            emergencyContact = "Next of kin",
        });

    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password = DemoPassword) =>
        client.PostAsJsonAsync("/api/auth/login", new { email, password });

    public static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string? rawCookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        if (rawCookie is not null)
        {
            request.Headers.Add("Cookie", $"{CookieName}={rawCookie}");
        }
        return client.SendAsync(request);
    }

    public static Task<HttpResponseMessage> LogoutAsync(HttpClient client, string accessToken, string? rawCookie)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (rawCookie is not null)
        {
            request.Headers.Add("Cookie", $"{CookieName}={rawCookie}");
        }
        return client.SendAsync(request);
    }

    /// <summary>Raw cookie VALUE from Set-Cookie, or null when no rr_refresh header is present.</summary>
    public static string? ExtractRefreshCookie(HttpResponseMessage response)
    {
        var header = FindSetCookieHeader(response);
        if (header is null)
        {
            return null;
        }
        var value = header[(CookieName.Length + 1)..];
        var end = value.IndexOf(';');
        return end >= 0 ? value[..end] : value;
    }

    /// <summary>The full Set-Cookie header line for rr_refresh, or null.</summary>
    public static string? FindSetCookieHeader(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith(CookieName + "=", StringComparison.Ordinal))
            : null;

    public static async Task<AuthSession> ReadSessionAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(json);
        var data = body.RootElement.GetProperty("data");
        var user = data.GetProperty("user");
        return new AuthSession(
            data.GetProperty("accessToken").GetString()!,
            data.GetProperty("expiresAtUtc").GetDateTimeOffset(),
            user.GetProperty("id").GetGuid(),
            user.GetProperty("email").GetString()!,
            user.GetProperty("roles").EnumerateArray().Select(r => r.GetString()!).ToArray());
    }

    public static HttpRequestMessage BearerRequest(HttpMethod method, string url, string accessToken)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    /// <summary>Register a fresh user and return its live session + refresh cookie.</summary>
    public static async Task<(AuthSession Session, string Cookie, string Email)> RegisterFreshUserAsync(HttpClient client)
    {
        var email = UniqueEmail();
        var response = await RegisterAsync(client, email);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        var session = await ReadSessionAsync(response);
        var cookie = ExtractRefreshCookie(response);
        Assert.NotNull(cookie);
        return (session, cookie!, email);
    }
}

internal sealed record AuthSession(string AccessToken, DateTimeOffset ExpiresAtUtc, Guid UserId, string Email,
    IReadOnlyList<string> Roles);
