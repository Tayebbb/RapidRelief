using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Shared.Contracts.Services;

public interface IShelterReadService
{
    Task<IReadOnlyList<ShelterSummaryDto>> GetSheltersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ShelterSummaryDto>> GetNearestAsync(GeoPoint origin, int count = 5, CancellationToken ct = default);
}
