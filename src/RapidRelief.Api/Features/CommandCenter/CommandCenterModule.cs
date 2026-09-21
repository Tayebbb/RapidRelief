using RapidRelief.Api.Infrastructure.Modules;

namespace RapidRelief.Api.Features.CommandCenter;

public sealed class CommandCenterModule : IFeatureModule
{
    public string Name => "CommandCenter";

    public void AddModule(IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        // No DbContext or services required for CommandCenter; it's a stateless aggregation layer.
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        RapidRelief.Api.Features.CommandCenter.Endpoints.CommandCenterEndpoints.Map(endpoints);
    }

    public Task MigrateAsync(IServiceProvider scopedServices, CancellationToken ct)
        => Task.CompletedTask; // No DB to migrate
}
