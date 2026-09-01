using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Sample.Data;
using RapidRelief.Api.Features.Sample.Endpoints;
using RapidRelief.Api.Features.Sample.Handlers;
using RapidRelief.Api.Infrastructure.Modules;
using RapidRelief.Shared.Contracts.Eventing;
using RapidRelief.Shared.Contracts.Events;

namespace RapidRelief.Api.Features.Sample;

/// <summary>The copy-me vertical slice (D-008): context + migration + endpoints + event handler.</summary>
public sealed class SampleModule : IFeatureModule
{
    public string Name => "Sample";

    public void AddModule(IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        // Npgsql ONLY outside Testing — the test factory injects its own SQLite options (B6 step 8).
        if (!env.IsEnvironment("Testing"))
        {
            var connectionString = config.GetConnectionString("Postgres");
            services.AddDbContext<SampleDbContext>(options =>
                options.UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsHistoryTable(SampleDbContext.MigrationsHistoryTableName)));
        }

        services.AddScoped<IEventHandler<PingCreated>, PingCreatedLoggingHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        PingEndpoints.Map(endpoints);
    }

    public Task MigrateAsync(IServiceProvider scopedServices, CancellationToken ct)
        => scopedServices.GetRequiredService<SampleDbContext>().Database.MigrateAsync(ct);
}
