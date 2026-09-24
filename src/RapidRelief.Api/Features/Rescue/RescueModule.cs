using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Rescue.Data;
using RapidRelief.Api.Infrastructure.Modules;
using RapidRelief.Shared.Contracts.Eventing;
using RapidRelief.Shared.Contracts.Events;
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

        services.AddSingleton(Services.AutoDispatchOptions.Read(config));
        services.AddScoped<Services.IAutoDispatchService, Services.AutoDispatchService>();
        services.AddScoped<Handlers.AutoDispatchIncidentAssessedHandler>();
        services.AddScoped<IEventHandler<IncidentAssessed>>(sp =>
            sp.GetRequiredService<Handlers.AutoDispatchIncidentAssessedHandler>());
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
