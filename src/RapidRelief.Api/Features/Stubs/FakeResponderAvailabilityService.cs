using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Stubs;

/// <summary>
/// Stub-yield fallback (B5) for consumers that weigh rescue capacity. Reports an unknown
/// registry rather than an empty one — "no teams exist" and "we cannot see the teams" must
/// not score the same.
/// </summary>
public sealed class FakeResponderAvailabilityService : IResponderAvailabilityService
{
    public Task<ResponderAvailabilityDto> GetAvailabilityAsync(
        GeoPoint? near = null, CancellationToken ct = default)
        => Task.FromResult(ResponderAvailabilityDto.Unknown);
}
