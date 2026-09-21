using System.Security.Claims;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Client.Common.Auth;

/// <summary>
/// Resolves the landing dashboard route according to user role:
/// Citizen -> "/c", Rescuer -> "/r", Government/Admin -> "/g".
/// </summary>
public static class AuthRouteHelper
{
    public static string GetDashboardRoute(ClaimsPrincipal? user, string? returnUrl = null)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) &&
            returnUrl.StartsWith('/') &&
            !returnUrl.StartsWith("/auth", StringComparison.OrdinalIgnoreCase) &&
            !returnUrl.Equals("/login", StringComparison.OrdinalIgnoreCase) &&
            !returnUrl.Equals("/register", StringComparison.OrdinalIgnoreCase))
        {
            return returnUrl.TrimStart('/');
        }

        if (user is not null)
        {
            if (user.IsInRole(Roles.Government))
            {
                return "g";
            }

            if (user.IsInRole(Roles.Rescuer))
            {
                return "r";
            }

            if (user.IsInRole(Roles.Citizen))
            {
                return "c";
            }
        }

        return "c";
    }

    public static string GetDashboardRoute(string? role, string? returnUrl = null)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) &&
            returnUrl.StartsWith('/') &&
            !returnUrl.StartsWith("/auth", StringComparison.OrdinalIgnoreCase) &&
            !returnUrl.Equals("/login", StringComparison.OrdinalIgnoreCase) &&
            !returnUrl.Equals("/register", StringComparison.OrdinalIgnoreCase))
        {
            return returnUrl.TrimStart('/');
        }

        if (string.Equals(role, Roles.Government, StringComparison.OrdinalIgnoreCase))
        {
            return "g";
        }

        if (string.Equals(role, Roles.Rescuer, StringComparison.OrdinalIgnoreCase))
        {
            return "r";
        }

        return "c";
    }
}
