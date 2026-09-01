using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Tests.Auth;

/// <summary>
/// Fail-closed pins for non-Dev/Testing environments: FakeAuth must never authenticate in
/// Production, and the host must refuse to start without a real JWT signing key.
/// </summary>
public sealed class ProductionAuthTests
{
    private static readonly string DummySigningKey = new('k', 64);

    private static WebApplicationFactory<Program> CreateProductionFactory(string? signingKey) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Jwt:SigningKey", signingKey);
        });

    [Fact]
    public async Task Production_whoami_ignores_dev_role_header_and_returns_401()
    {
        // Also proves the host BOOTS in Production when a >=32-byte signing key is present.
        using var factory = CreateProductionFactory(DummySigningKey);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(FakeAuthHandler.HeaderName, Roles.Admin);

        var response = await client.GetAsync("/api/foundation/whoami");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void Production_host_without_jwt_signing_key_fails_to_start()
    {
        using var factory = CreateProductionFactory(signingKey: "");

        var ex = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(ex);
        Assert.Contains("Jwt:SigningKey", ex!.ToString());
    }

    [Fact]
    public void Production_host_with_short_jwt_signing_key_fails_to_start()
    {
        using var factory = CreateProductionFactory(signingKey: "too-short");

        var ex = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(ex);
        Assert.Contains("Jwt:SigningKey", ex!.ToString());
    }
}
