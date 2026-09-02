using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Incidents.Data;
using RapidRelief.Api.Infrastructure.Modules;

namespace RapidRelief.Api.Features.Incidents;

public sealed class IncidentsModule : IFeatureModule
{
    public string Name => "Incidents";

    public void AddModule(IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        if (!env.IsEnvironment("Testing"))
        {
            services.AddDbContext<IncidentsDbContext>(options =>
                options.UseNpgsql(config.GetConnectionString("Postgres"), npgsql =>
                    npgsql.MigrationsHistoryTable(IncidentsDbContext.MigrationsHistoryTableName)));
        }
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Minimal incidents endpoint routes
    }

    public async Task MigrateAsync(IServiceProvider scopedServices, CancellationToken ct)
    {
        await scopedServices.GetRequiredService<IncidentsDbContext>().Database.MigrateAsync(ct);
    }
}
