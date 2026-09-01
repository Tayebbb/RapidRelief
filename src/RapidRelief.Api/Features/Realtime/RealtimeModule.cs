using RapidRelief.Api.Infrastructure.Modules;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Realtime;

/// <summary>Tayeb's F9 lane. Plain Add* — the real-service slot; SignalR replaces the no-op here later.</summary>
public sealed class RealtimeModule : IFeatureModule
{
    public string Name => "Realtime";

    public void AddModule(IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        services.AddSingleton<IRealtimeNotifier, NoOpRealtimeNotifier>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
