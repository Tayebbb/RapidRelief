using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using RapidRelief.Api.Features.Ai.Data;
using RapidRelief.Api.Features.Alerts.Data;
using RapidRelief.Api.Features.Auth.Data;
using RapidRelief.Api.Features.Auth.Services;
using RapidRelief.Api.Features.Realtime.Data;
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
    /// <summary>Testing mints/validates REAL JWTs — MigrationRunner (and appsettings.Development) never runs here.</summary>
    public const string TestSigningKey = "tttttttttttttttttttttttttttttttttttttttttttttttttttttttttttttttt";

    private readonly List<SqliteConnection> _connections = [];
    private readonly string _storageRoot =
        Path.Combine(Path.GetTempPath(), "rapidrelief-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:SigningKey", TestSigningKey);
        // D-018: V3 hashes embed the per-hash iteration count, so a low test count is safe.
        builder.UseSetting("Auth:PasswordHasherIterations", "10000");
        // Keep uploads out of the repo tree and make the oversize test cheap (64 KiB cap).
        builder.UseSetting("FileStorage:Root", _storageRoot);
        builder.UseSetting("FileStorage:MaxSizeBytes", "65536");
        builder.ConfigureServices(services =>
        {
            AddSqliteContext<SampleDbContext>(services);
            AddSqliteContext<AuthDbContext>(services);
            AddSqliteContext<RapidRelief.Api.Features.Shelters.Data.OpsDbContext>(services);
            AddSqliteContext<AiDbContext>(services);
            AddSqliteContext<AlertsDbContext>(services);
            AddSqliteContext<NotificationsDbContext>(services);
            AddSqliteContext<RapidRelief.Api.Features.Incidents.Data.IncidentsDbContext>(services);
            AddSqliteContext<RapidRelief.Api.Features.Rescue.Data.RescueDbContext>(services);
            AddSqliteContext<RapidRelief.Api.Features.Relief.Data.ReliefDbContext>(services);
            AddSqliteContext<RapidRelief.Api.Features.Audit.Data.AuditDbContext>(services);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        EnsureCreated<SampleDbContext>(host);
        EnsureCreated<AuthDbContext>(host);
        EnsureCreated<RapidRelief.Api.Features.Shelters.Data.OpsDbContext>(host);
        EnsureCreated<AiDbContext>(host);
        EnsureCreated<AlertsDbContext>(host);
        EnsureCreated<NotificationsDbContext>(host);
        EnsureCreated<RapidRelief.Api.Features.Incidents.Data.IncidentsDbContext>(host);
        EnsureCreated<RapidRelief.Api.Features.Rescue.Data.RescueDbContext>(host);
        EnsureCreated<RapidRelief.Api.Features.Relief.Data.ReliefDbContext>(host);
        EnsureCreated<RapidRelief.Api.Features.Audit.Data.AuditDbContext>(host);

        // MigrationRunner is skipped in Testing, so module seeding never runs — seed here (risk 3).
        using (var scope = host.Services.CreateScope())
        {
            AuthSeeder.SeedAsync(scope.ServiceProvider, CancellationToken.None).GetAwaiter().GetResult();
            RapidRelief.Api.Tests.Shelters.OpsSeeder.SeedAsync(scope.ServiceProvider, CancellationToken.None).GetAwaiter().GetResult();
            RapidRelief.Api.Features.Incidents.Services.IncidentSeeder
                .SeedAsync(scope.ServiceProvider, CancellationToken.None).GetAwaiter().GetResult();
        }

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

        // Named shared-cache in-memory DB, unique per host build: every scope opens its OWN
        // connection — a single shared SqliteConnection is NOT thread-safe, and parallel
        // requests (auth rotation-race test) or the F8 background worker corrupt its internal
        // command list (dispose-time NRE in SqliteConnection.RemoveCommand). The kept-open
        // anchor keeps the database alive; Pooling=False makes teardown deterministic.
        var connectionString =
            $"Data Source={typeof(TContext).Name}-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False";
        var anchor = new SqliteConnection(connectionString);
        anchor.Open();
        lock (_connections)
        {
            _connections.Add(anchor);
        }

        services.AddDbContext<TContext>(options => options.UseSqlite(connectionString));
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
            try
            {
                if (Directory.Exists(_storageRoot))
                {
                    Directory.Delete(_storageRoot, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort temp cleanup only (AV scanners briefly lock fresh files on Windows).
            }
        }
    }
}
