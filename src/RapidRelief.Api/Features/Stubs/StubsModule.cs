using Microsoft.Extensions.DependencyInjection.Extensions;
using RapidRelief.Api.Infrastructure.Modules;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Stubs;

/// <summary>
/// Registers the deterministic fakes LAST (Order = int.MaxValue) via TryAdd* — the stub-yield
/// rule (B5): any real implementation registered by its owning module automatically wins, and
/// the fake silently resumes if that module is ever pulled. Stubs stay alive all semester (§4.5).
/// </summary>
public sealed class StubsModule : IFeatureModule
{
    public string Name => "Stubs";

    public int Order => int.MaxValue;

    public void AddModule(IServiceCollection services, IConfiguration config, IHostEnvironment env)
    {
        services.TryAddSingleton<IIncidentReadService, FakeIncidentReadService>();
        services.TryAddSingleton<IShelterReadService, FakeShelterReadService>();
        services.TryAddSingleton<IRegistryReadService, FakeRegistryReadService>();
        services.TryAddSingleton<IUserAdminService, FakeUserAdminService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
    }
}
