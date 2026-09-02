using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Infrastructure.Auth;

/// <summary>
/// Dev/Testing-only header auth: X-Dev-Role ∈ Roles.All (case-insensitive) → fixed seed
/// principal; header absent → NoResult; invalid value → Fail. Never registered elsewhere.
/// </summary>
public sealed class FakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "FakeAuth";
    public const string HeaderName = "X-Dev-Role";

    /// <summary>Fixed seed GUIDs per role — must match §5 seeded identities and future stub users.</summary>
    public static readonly IReadOnlyDictionary<string, Guid> SeedUserIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
    {
        [Roles.Citizen] = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        [Roles.Rescuer] = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        [Roles.Government] = Guid.Parse("33333333-3333-3333-3333-333333333333"),
    };

    public FakeAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var requested = values.ToString();
        var role = Roles.All.FirstOrDefault(r => string.Equals(r, requested, StringComparison.OrdinalIgnoreCase));
        if (role is null)
        {
            if (string.Equals(requested, "Admin", StringComparison.OrdinalIgnoreCase) || string.Equals(requested, "NGO", StringComparison.OrdinalIgnoreCase))
            {
                role = Roles.Government;
            }
            else if (string.Equals(requested, "Rescue", StringComparison.OrdinalIgnoreCase))
            {
                role = Roles.Rescuer;
            }
            else
            {
                return Task.FromResult(AuthenticateResult.Fail($"Unknown dev role '{requested}'."));
            }
        }

        var email = $"{requested.ToLowerInvariant()}1@rr.dev";
        var userId = string.Equals(requested, "Admin", StringComparison.OrdinalIgnoreCase)
            ? Guid.Parse("33333333-3333-3333-3333-333333333334")
            : string.Equals(requested, "Rescue", StringComparison.OrdinalIgnoreCase)
                ? Guid.Parse("22222222-2222-2222-2222-222222222224")
                : SeedUserIds[role];

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Name, email),
            new Claim(ClaimTypes.Role, role),
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }
}
