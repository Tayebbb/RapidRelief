using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;

namespace RapidRelief.Client.Common.Auth;

/// <summary>
/// In-memory JWT session (blueprint B10.1). The access token never touches browser storage
/// APIs (D-012); a page reload restores the session via the silent boot refresh instead.
/// </summary>
public sealed class JwtAuthStateProvider : AuthenticationStateProvider
{
    private static readonly ClaimsPrincipal Anonymous = new(new ClaimsIdentity());

    private ClaimsPrincipal _user = Anonymous;

    public string? AccessToken { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public bool HasSession => AccessToken is not null;

    public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
        Task.FromResult(new AuthenticationState(_user));

    public void SetSession(string accessToken, DateTimeOffset expiresAtUtc)
    {
        AccessToken = accessToken;
        ExpiresAtUtc = expiresAtUtc;
        _user = ParsePrincipal(accessToken);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public void ClearSession()
    {
        if (AccessToken is null)
        {
            return; // idempotent — no spurious re-render at anonymous boot
        }

        AccessToken = null;
        ExpiresAtUtc = default;
        _user = Anonymous;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    /// <summary>
    /// Manual payload parse — no client JWT package. The server's default outbound claim map emits
    /// nameid/unique_name/role, but accept sub/email too, and role as string OR array (risk 6).
    /// </summary>
    private static ClaimsPrincipal ParsePrincipal(string accessToken)
    {
        try
        {
            var segments = accessToken.Split('.');
            if (segments.Length < 2)
            {
                return Anonymous;
            }

            using var payload = JsonDocument.Parse(DecodeBase64Url(segments[1]));
            var root = payload.RootElement;
            var claims = new List<Claim>();

            if (TryGetString(root, "nameid", out var userId) || TryGetString(root, "sub", out userId))
            {
                claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
            }

            if (TryGetString(root, "unique_name", out var name) || TryGetString(root, "email", out name))
            {
                claims.Add(new Claim(ClaimTypes.Name, name));
            }

            if (root.TryGetProperty("role", out var roles))
            {
                switch (roles.ValueKind)
                {
                    case JsonValueKind.String:
                        claims.Add(new Claim(ClaimTypes.Role, roles.GetString()!));
                        break;
                    case JsonValueKind.Array:
                        claims.AddRange(roles.EnumerateArray()
                            .Where(role => role.ValueKind == JsonValueKind.String)
                            .Select(role => new Claim(ClaimTypes.Role, role.GetString()!)));
                        break;
                }
            }

            // A non-null authenticationType is what makes IsAuthenticated true.
            return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "jwt"));
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return Anonymous; // an unparseable token stays anonymous instead of crashing boot
        }
    }

    private static bool TryGetString(JsonElement root, string propertyName, [NotNullWhen(true)] out string? value)
    {
        if (root.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString();
            return value is not null;
        }

        value = null;
        return false;
    }

    private static byte[] DecodeBase64Url(string segment)
    {
        var padded = segment.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => string.Empty,
        };
        return Convert.FromBase64String(padded);
    }
}
