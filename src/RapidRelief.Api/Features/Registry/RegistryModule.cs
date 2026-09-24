using FluentValidation;
using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Registry.Data;
using RapidRelief.Api.Features.Registry.Endpoints;
using RapidRelief.Api.Features.Registry.Services;
using RapidRelief.Api.Infrastructure.Modules;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Registry;

public sealed class RegistryModule : IFeatureModule
{
    public string Name => "Registry";

    public int Order => 0;

    public void AddModule(IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        if (!env.IsEnvironment("Testing"))
        {
            services.AddDbContext<RegistryDbContext>(options =>
                options.UseNpgsql(config.GetConnectionString("Postgres"), npgsql =>
                    npgsql.MigrationsHistoryTable(RegistryDbContext.MigrationsHistoryTableName)));
        }

        services.AddScoped<IRegistryReadService, RegistryReadService>();
        services.AddValidatorsFromAssemblyContaining<CreateHospitalValidator>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints) => RegistryEndpoints.Map(endpoints);

    public async Task MigrateAsync(IServiceProvider scopedServices, CancellationToken ct)
    {
        var db = scopedServices.GetRequiredService<RegistryDbContext>();
        await db.Database.MigrateAsync(ct);
        await RegistrySeeder.SeedAsync(scopedServices, ct);
    }
}
