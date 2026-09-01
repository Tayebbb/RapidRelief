namespace RapidRelief.Api.Infrastructure.Modules;

/// <summary>
/// Self-registration contract for vertical-slice feature modules (server-only, NOT in Shared).
/// Discovered by reflection; sorted by <see cref="Order"/> then <see cref="Name"/>.
/// </summary>
public interface IFeatureModule
{
    string Name { get; }

    /// <summary>Stubs override with int.MaxValue so TryAdd* registrations always yield to real ones.</summary>
    int Order => 0;

    void AddModule(IServiceCollection services, IConfiguration config, IHostEnvironment env);

    void MapEndpoints(IEndpointRouteBuilder endpoints);

    /// <summary>Each context-owning module migrates its own DbContext only (arrives with chunk 2).</summary>
    Task MigrateAsync(IServiceProvider scopedServices, CancellationToken ct) => Task.CompletedTask;
}
