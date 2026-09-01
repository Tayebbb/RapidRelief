using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using RapidRelief.Api.Features.Sample.Data;
using RapidRelief.Api.Infrastructure.Persistence;

namespace RapidRelief.Api.Tests;

/// <summary>
/// Boots the real Program composition under env "Testing" (rate limiter + Npgsql registration
/// + MigrationRunner all skipped). Each DbContext gets its OWN kept-open SQLite :memory:
/// connection + EnsureCreated — the pattern scales one line per future context (§4.4).
/// </summary>
public sealed class TestingWebAppFactory : WebApplicationFactory<Program>
{
    private readonly List<SqliteConnection> _connections = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            AddSqliteContext<SampleDbContext>(services);
            // Future contexts (AuthDbContext, IncidentsDbContext, …): add one line here
            // and one EnsureCreated<TContext> line in CreateHost.
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        EnsureCreated<SampleDbContext>(host);

        // EnsureCreated succeeded ⇒ the relational store is real and reachable, so the
        // D-005 gate must open: Testing reports dbConnected=true (MigrationRunner is skipped).
        host.Services.GetRequiredService<DatabaseHealth>().PostgresAvailable = true;

        return host;
    }

    /// <summary>Kills any Npgsql registration (defensive) and rebinds the context to SQLite.</summary>
    private void AddSqliteContext<TContext>(IServiceCollection services) where TContext : DbContext
    {
        services.RemoveAll<DbContextOptions<TContext>>();
        services.RemoveAll<TContext>();

        // Kept open on purpose: a :memory: database lives exactly as long as its connection.
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        _connections.Add(connection);

        services.AddDbContext<TContext>(options => options.UseSqlite(connection));
    }

    private static void EnsureCreated<TContext>(IHost host) where TContext : DbContext
    {
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TContext>().Database.EnsureCreated();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            foreach (var connection in _connections)
            {
                connection.Dispose();
            }
        }
    }
}
