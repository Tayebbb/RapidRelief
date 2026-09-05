using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Shelters.Data;
using RapidRelief.Api.Infrastructure.Modules;

namespace RapidRelief.Api.Features.Shelters;

public sealed class SheltersModule : IFeatureModule
{
    public string Name => "Shelters";

    public void AddModule(IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        // Npgsql ONLY outside Testing — the test factory injects its own SQLite options.
        if (!env.IsEnvironment("Testing"))
        {
            var connectionString = config.GetConnectionString("Postgres");
            services.AddDbContext<OpsDbContext>(options =>
                options.UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable(OpsDbContext.MigrationsHistoryTableName)));
        }
        
        services.AddScoped<RapidRelief.Shared.Contracts.Services.IShelterReadService, RapidRelief.Api.Features.Shelters.Services.ShelterReadService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RapidRelief.Api.Features.Shelters.Endpoints.SheltersEndpoints.Map(endpoints);
        RapidRelief.Api.Features.Shelters.Endpoints.SheltersAiEndpoints.Map(endpoints);
    }

    public async Task MigrateAsync(IServiceProvider scopedServices, CancellationToken ct)
    {
        await scopedServices.GetRequiredService<OpsDbContext>().Database.MigrateAsync(ct);
        await Services.ShelterSeeder.SeedAsync(scopedServices, ct);
    }
}
