using Microsoft.AspNetCore.Authorization;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Infrastructure.Auth;

/// <summary>Role policies only — NO scheme names in policies (MultiAuth forwards schemes).</summary>
public static class AuthPolicies
{
    public const string RequireAdmin = "RequireAdmin";
    public const string RequireRescue = "RequireRescue";
    public const string RequireCitizen = "RequireCitizen";
    public const string RequireNgo = "RequireNgo";

    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(RequireAdmin, p => p.RequireRole(Roles.Admin));
        options.AddPolicy(RequireRescue, p => p.RequireRole(Roles.Rescue));
        options.AddPolicy(RequireCitizen, p => p.RequireRole(Roles.Citizen));
        options.AddPolicy(RequireNgo, p => p.RequireRole(Roles.Ngo));
    }
}
