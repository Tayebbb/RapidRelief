using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Audit.Data;
using RapidRelief.Api.Features.Audit.Endpoints;
using RapidRelief.Api.Features.Audit.Handlers;
using RapidRelief.Api.Features.Audit.Services;
using RapidRelief.Api.Infrastructure.Modules;
using RapidRelief.Shared.Contracts.Eventing;
using RapidRelief.Shared.Contracts.Events;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Audit;

public sealed class AuditModule : IFeatureModule
{
    public string Name => "Audit";

    public void AddModule(IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        if (!env.IsEnvironment("Testing"))
        {
            services.AddDbContext<AuditDbContext>(options =>
                options.UseNpgsql(config.GetConnectionString("Postgres"), npgsql =>
                    npgsql.MigrationsHistoryTable(AuditDbContext.MigrationsHistoryTableName)));
        }

        services.AddHttpContextAccessor();
        services.AddScoped<IAuditTrail, AuditTrail>();

        services.AddScoped<IEventHandler<IncidentVerified>, IncidentVerifiedAuditHandler>();
        services.AddScoped<IEventHandler<MissionAssigned>, MissionAssignedAuditHandler>();
        services.AddScoped<IEventHandler<MissionStatusChanged>, MissionStatusAuditHandler>();
        services.AddScoped<IEventHandler<AlertPublished>, AlertPublishedAuditHandler>();
        services.AddScoped<IEventHandler<ReliefStatusChanged>, ReliefStatusAuditHandler>();
        services.AddScoped<IEventHandler<AuthEvent>, AuthEventAuditHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => AuditEndpoints.Map(endpoints);

    public async Task MigrateAsync(IServiceProvider scopedServices, CancellationToken ct)
    {
        await scopedServices.GetRequiredService<AuditDbContext>().Database.MigrateAsync(ct);
    }
}
