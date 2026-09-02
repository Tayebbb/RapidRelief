using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Tests.Realtime;

/// <summary>
/// The "realtime" policy partitions by caller id (Foundation/RateLimitPartitionTests), but that
/// only engages if the limiter runs AFTER authentication — otherwise HttpContext.User is
/// anonymous and every caller silently falls back to the shared IP bucket.
/// </summary>
public sealed class RealtimeRateLimitPartitionTests
{
    private const string Route = "/api/realtime/notifications/unread-count";

    [Fact] // Development registers the limiter (it is skipped in Testing); empty connection
    // string ⇒ degraded boot (D-005), so responses are 503 — anything but 429 means "not limited".
    public async Task One_users_exhausted_realtime_budget_does_not_429_another_user_on_the_same_ip()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Postgres", "");
            builder.UseSetting("RateLimiting:Realtime:PermitLimit", "3");
            builder.UseSetting("RateLimiting:Realtime:WindowSeconds", "60");
        });

        using var admin = CreateClientAs(factory, Roles.Admin);
        using var citizen = CreateClientAs(factory, Roles.Citizen);

        var adminStatuses = new List<HttpStatusCode>();
        for (var i = 0; i < 4; i++)
        {
            adminStatuses.Add((await admin.GetAsync(Route)).StatusCode);
        }

        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, adminStatuses.Take(3));
        Assert.Equal(HttpStatusCode.TooManyRequests, adminStatuses[3]);

        var citizenStatus = (await citizen.GetAsync(Route)).StatusCode;

        Assert.NotEqual(HttpStatusCode.TooManyRequests, citizenStatus);
    }

    private static HttpClient CreateClientAs(WebApplicationFactory<Program> factory, string role)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(FakeAuthHandler.HeaderName, role);
        return client;
    }
}
