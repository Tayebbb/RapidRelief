using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Alerts.Data;
using RapidRelief.Api.Features.Alerts.Endpoints;
using RapidRelief.Api.Infrastructure.Modules;

namespace RapidRelief.Api.Features.Alerts;

public sealed class AlertsModule : IFeatureModule
{
    public string Name => "Alerts";

    public void AddModule(IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        if (!env.IsEnvironment("Testing"))
        {
            services.AddDbContext<AlertsDbContext>(options =>
                options.UseNpgsql(config.GetConnectionString("Postgres"), npgsql =>
                    npgsql.MigrationsHistoryTable(AlertsDbContext.MigrationsHistoryTableName)));
        }
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => AlertsEndpoints.Map(endpoints);

    public Task MigrateAsync(IServiceProvider scopedServices, CancellationToken ct) =>
        scopedServices.GetRequiredService<AlertsDbContext>().Database.MigrateAsync(ct);
}
