using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using RapidRelief.Api.Infrastructure.RateLimiting;

namespace RapidRelief.Api.Tests.Foundation;

/// <summary>
/// D-011 partitions per IP, which is correct for anonymous surfaces. Authenticated realtime
/// polling is different: users behind one NAT/proxy would share a bucket and lose the
/// permanent polling fallback, so the caller's own id has to win when there is one.
/// </summary>
public sealed class RateLimitPartitionTests
{
    private static HttpContext Context(string? userId, string? ip)
    {
        var httpContext = new DefaultHttpContext();
        if (userId is not null)
        {
            httpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "test"));
        }

        httpContext.Connection.RemoteIpAddress = ip is null ? null : IPAddress.Parse(ip);
        return httpContext;
    }

    [Fact]
    public void An_authenticated_caller_is_partitioned_by_user_id_not_by_ip()
    {
        var key = RateLimitPartitions.UserOrIp(Context("d3ad0000-0000-0000-0000-000000000001", "10.0.0.7"));

        Assert.Contains("d3ad0000-0000-0000-0000-000000000001", key, StringComparison.Ordinal);
        Assert.DoesNotContain("10.0.0.7", key, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_users_behind_one_shared_ip_get_their_own_buckets()
    {
        var first = RateLimitPartitions.UserOrIp(Context("11111111-1111-1111-1111-111111111111", "10.0.0.7"));
        var second = RateLimitPartitions.UserOrIp(Context("22222222-2222-2222-2222-222222222222", "10.0.0.7"));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void An_anonymous_caller_falls_back_to_the_remote_ip()
        => Assert.Contains("10.0.0.7", RateLimitPartitions.UserOrIp(Context(userId: null, "10.0.0.7")),
            StringComparison.Ordinal);

    [Fact]
    public void A_caller_with_neither_lands_in_one_shared_bucket()
        => Assert.Equal(
            RateLimitPartitions.UserOrIp(Context(userId: null, ip: null)),
            RateLimitPartitions.UserOrIp(Context(userId: null, ip: null)));
}
