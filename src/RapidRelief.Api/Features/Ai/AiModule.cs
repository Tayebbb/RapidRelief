using Microsoft.Extensions.DependencyInjection.Extensions;
using RapidRelief.Api.Infrastructure.Modules;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Ai;

/// <summary>
/// Tayeb's F8 lane. Registers the permanent rule-based fallback with plain Add* — this is the
/// real-service slot, demonstrating the stub-yield rule from the winning side (B5).
/// </summary>
public sealed class AiModule : IFeatureModule
{
    public string Name => "Ai";

    public void AddModule(IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        // TryAdd: tests or future composition may pin a fixed TimeProvider first.
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IAiAnalysisService, RuleBasedAiAnalysisService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
