using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Shared.Contracts.Services;

/// <summary>
/// Read-only rescue capacity, owned by the rescue slice. Implementations must never throw —
/// an unknown capacity is <see cref="ResponderAvailabilityDto.Unknown"/>, because a priority
/// score has to be computable even when the rescue store is unreachable.
/// </summary>
public interface IResponderAvailabilityService
{
    Task<ResponderAvailabilityDto> GetAvailabilityAsync(GeoPoint? near = null, CancellationToken ct = default);
}
