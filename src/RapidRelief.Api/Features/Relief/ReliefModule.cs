using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Relief.Data;
using RapidRelief.Api.Infrastructure.Modules;

namespace RapidRelief.Api.Features.Relief;

public sealed class ReliefModule : IFeatureModule
{
    public string Name => "Relief";

    public void AddModule(IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        if (!env.IsEnvironment("Testing"))
        {
            services.AddDbContext<ReliefDbContext>(options =>
                options.UseNpgsql(config.GetConnectionString("Postgres"), npgsql =>
                    npgsql.MigrationsHistoryTable(ReliefDbContext.MigrationsHistoryTableName)));
        }
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        Endpoints.ReliefEndpoints.Map(endpoints);
    }

    public async Task MigrateAsync(IServiceProvider scopedServices, CancellationToken ct)
    {
        await scopedServices.GetRequiredService<ReliefDbContext>().Database.MigrateAsync(ct);
    }
}
