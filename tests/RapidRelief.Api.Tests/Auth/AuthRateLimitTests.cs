using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace RapidRelief.Api.Tests.Auth;

/// <summary>Blueprint TEST PLAN ㊲–㊳ (D-011 mandatory call sites).</summary>
public sealed class AuthRateLimitTests : IClassFixture<TestingWebAppFactory>
{
    private readonly TestingWebAppFactory _factory;

    public AuthRateLimitTests(TestingWebAppFactory factory) => _factory = factory;

    [Theory] // ㊲ — metadata pin via EndpointDataSource
    [InlineData("/api/auth/register")]
    [InlineData("/api/auth/login")]
    [InlineData("/api/auth/refresh")]
    public void Anonymous_auth_endpoints_carry_the_auth_rate_limit_policy(string route)
    {
        var endpoints = _factory.Services.GetServices<EndpointDataSource>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(e => string.Equals(e.RoutePattern.RawText, route, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(endpoints);
        Assert.All(endpoints, e =>
        {
            var metadata = e.Metadata.GetMetadata<EnableRateLimitingAttribute>();
            Assert.NotNull(metadata);
            Assert.Equal("auth", metadata!.PolicyName);
        });
    }

    [Fact] // ㊳ — live pin: Development env registers the limiter (default Auth:PermitLimit = 10)
    // Assumes all 11 posts land inside ONE fixed window (Auth:WindowSeconds default = 60 s);
    // the loop takes well under a second, so a rollover mid-run is effectively impossible —
    // if this ever flakes, check the window assumption first.
    public async Task Eleventh_rapid_login_post_returns_429_in_development()
    {
        // Empty connection string ⇒ MigrationRunner fails fast ⇒ degraded boot (D-005), no Postgres needed.
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Postgres", "");
        });
        using var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 11; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login",
                new { email = "citizen1@rr.dev", password = "Demo!123" });
            statuses.Add(response.StatusCode);
        }

        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, statuses.Take(10));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[10]);
    }
}
