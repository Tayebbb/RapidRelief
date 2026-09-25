using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Shared.Contracts.Services;

public interface ISafetyZonesReadService
{
    Task<IReadOnlyList<SafetyZoneDto>> GetActiveSafetyZonesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<RoadClosureDto>> GetActiveRoadClosuresAsync(CancellationToken ct = default);
}
