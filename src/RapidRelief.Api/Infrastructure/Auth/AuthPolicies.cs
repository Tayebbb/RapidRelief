using Microsoft.AspNetCore.Authorization;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Infrastructure.Auth;

/// <summary>Role policies only — NO scheme names in policies (MultiAuth forwards schemes).</summary>
public static class AuthPolicies
{
    public const string RequireGovernment = "RequireGovernment";
    public const string RequireRescuer = "RequireRescuer";
    public const string RequireCitizen = "RequireCitizen";

    // Backward-compatible aliases
    public const string RequireAdmin = "RequireGovernment";
    public const string RequireRescue = "RequireRescuer";
    public const string RequireNgo = "RequireGovernment";

    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(RequireGovernment, p => p.RequireRole(Roles.Government));
        options.AddPolicy(RequireRescuer, p => p.RequireRole(Roles.Rescuer));
        options.AddPolicy(RequireCitizen, p => p.RequireRole(Roles.Citizen));

        // Legacy compatibility
        options.AddPolicy("RequireAdmin", p => p.RequireRole(Roles.Government));
        options.AddPolicy("RequireRescue", p => p.RequireRole(Roles.Rescuer));
        options.AddPolicy("RequireNgo", p => p.RequireRole(Roles.Government));
    }
}
