using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Rescue.Data;
using RapidRelief.Api.Infrastructure.Modules;
using RapidRelief.Shared.Contracts.Services;

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

        // Displaces the stub so priority scoring and the assistant see real rescue capacity.
        services.AddScoped<IResponderAvailabilityService, Services.ResponderAvailabilityService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Endpoints.RescueEndpoints.Map(endpoints);
    }

    public async Task MigrateAsync(IServiceProvider scopedServices, CancellationToken ct)
    {
        await scopedServices.GetRequiredService<RescueDbContext>().Database.MigrateAsync(ct);
        await Services.RescueTeamSeeder.SeedAsync(scopedServices, ct);
    }
}
