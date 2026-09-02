using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Rescue.Data;
using RapidRelief.Api.Infrastructure.Modules;

namespace RapidRelief.Api.Features.Rescue;

public sealed class RescueModule : IFeatureModule
{
    public string Name => "Rescue";

    public void AddModule(IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        if (!env.IsEnvironment("Testing"))
        {
            services.AddDbContext<RescueDbContext>(options =>
                options.UseNpgsql(config.GetConnectionString("Postgres"), npgsql =>
                    npgsql.MigrationsHistoryTable(RescueDbContext.MigrationsHistoryTableName)));
        }
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Minimal rescue endpoint routes
    }

    public async Task MigrateAsync(IServiceProvider scopedServices, CancellationToken ct)
    {
        await scopedServices.GetRequiredService<RescueDbContext>().Database.MigrateAsync(ct);
    }
}
