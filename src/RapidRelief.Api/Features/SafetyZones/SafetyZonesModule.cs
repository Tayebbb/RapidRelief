using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.SafetyZones.Data;
using RapidRelief.Api.Features.SafetyZones.Endpoints;
using RapidRelief.Api.Features.SafetyZones.Services;
using RapidRelief.Api.Infrastructure.Modules;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.SafetyZones;

public sealed class SafetyZonesModule : IFeatureModule
{
    public string Name => "SafetyZones";

    public void AddModule(IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        // Npgsql ONLY outside Testing — the test factory injects its own SQLite options.
        if (!env.IsEnvironment("Testing"))
        {
            var connectionString = config.GetConnectionString("Postgres");
            services.AddDbContext<SafetyZonesDbContext>(options =>
                options.UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable(SafetyZonesDbContext.MigrationsHistoryTableName)));
        }

        services.AddScoped<ISafetyZonesReadService, SafetyZonesReadService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        SafetyZonesEndpoints.Map(endpoints);
    }

    public async Task MigrateAsync(IServiceProvider scopedServices, CancellationToken ct)
    {
        await scopedServices.GetRequiredService<SafetyZonesDbContext>().Database.MigrateAsync(ct);
        await SafetyZonesSeeder.SeedAsync(scopedServices, ct);
    }
}
