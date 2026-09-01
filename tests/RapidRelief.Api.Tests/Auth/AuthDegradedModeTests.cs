using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Tests.Auth;

/// <summary>Blueprint TEST PLAN ㊵ — D-005 pin: all auth endpoints are DB-backed ⇒ 503 degraded.</summary>
public sealed class AuthDegradedModeTests
{
    [Fact]
    public async Task All_auth_endpoints_return_503_when_database_unavailable_while_fake_auth_stays_alive()
    {
        // Own factory instance — flipping the health flag must never poison the shared fixture.
        using var factory = new TestingWebAppFactory();
        factory.Services.GetRequiredService<DatabaseHealth>().PostgresAvailable = false;
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(FakeAuthHandler.HeaderName, Roles.Admin);

        var register = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = "degraded@rr.dev",
            password = "Demo!123",
            displayName = "Degraded",
        });
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "a@b.c", password = "Demo!123" });
        var refresh = await client.PostAsync("/api/auth/refresh", content: null);
        var users = await client.GetAsync("/api/auth/users");
        var profile = await client.GetAsync("/api/auth/profile");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, register.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, login.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, users.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, profile.StatusCode);
        Assert.Equal("application/problem+json", login.Content.Headers.ContentType?.MediaType);

        // FakeAuth keeps the demo alive in degraded mode (§4.5) — stub-backed endpoints still work.
        var whoami = await client.GetAsync("/api/foundation/whoami");
        Assert.Equal(HttpStatusCode.OK, whoami.StatusCode);
    }
}
