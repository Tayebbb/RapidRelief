using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace RapidRelief.Api.Tests;

/// <summary>
/// Boots the real Program composition under env "Testing" (rate limiter skipped,
/// FakeAuth enabled). Chunk 2 extends this with per-context SQLite :memory: wiring.
/// </summary>
public sealed class TestingWebAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }
}
