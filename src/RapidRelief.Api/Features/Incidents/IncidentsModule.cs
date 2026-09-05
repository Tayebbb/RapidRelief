using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Incidents.Data;
using RapidRelief.Api.Features.Incidents.Endpoints;
using RapidRelief.Api.Features.Incidents.Handlers;
using RapidRelief.Api.Features.Incidents.Services;
using RapidRelief.Api.Infrastructure.Modules;
using RapidRelief.Shared.Contracts.Eventing;
using RapidRelief.Shared.Contracts.Events;
using RapidRelief.Shared.Contracts.Services;

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

        // Displaces the F0 stub: every consumer now reads live incidents through the contract.
        services.AddScoped<IIncidentReadService, IncidentReadService>();

        services.AddScoped<IEventHandler<IncidentCreated>, IncidentCreatedNotificationHandler>();
        services.AddScoped<IEventHandler<IncidentAssessed>, IncidentAssessedProjectionHandler>();
        services.AddScoped<IEventHandler<MissionAssigned>, MissionAssignedProjectionHandler>();
        services.AddScoped<IEventHandler<MissionStatusChanged>, MissionStatusProjectionHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => IncidentsEndpoints.Map(endpoints);

    public async Task MigrateAsync(IServiceProvider scopedServices, CancellationToken ct)
    {
        await scopedServices.GetRequiredService<IncidentsDbContext>().Database.MigrateAsync(ct);
        await IncidentSeeder.SeedAsync(scopedServices, ct);
    }
}
