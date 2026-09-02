using System.Security.Claims;

namespace RapidRelief.Api.Infrastructure.RateLimiting;

/// <summary>Partition-key helpers for the named rate-limit policies registered in Program.</summary>
public static class RateLimitPartitions
{
    private const string Unknown = "unknown";

    /// <summary>
    /// Per-caller when the request is authenticated, per-IP otherwise. Authenticated polling
    /// surfaces must not partition by IP alone: everyone behind one NAT/proxy would share a
    /// bucket and lose the permanent polling fallback (D-011 covers the anonymous surfaces).
    /// </summary>
    public static string UserOrIp(HttpContext httpContext)
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrEmpty(userId)
            ? $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? Unknown}"
            : $"user:{userId}";
    }
}
