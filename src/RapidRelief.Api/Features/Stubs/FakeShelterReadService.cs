using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Stubs;

/// <summary>Returns the 8 seeded shelters; GetNearestAsync = Haversine sort (blueprint B4).</summary>
public sealed class FakeShelterReadService : IShelterReadService
{
    public Task<IReadOnlyList<ShelterSummaryDto>> GetSheltersAsync(CancellationToken ct = default)
        => Task.FromResult(SeedData.DhakaSeedData.Shelters);

    public Task<IReadOnlyList<ShelterSummaryDto>> GetNearestAsync(GeoPoint origin, int count = 5, CancellationToken ct = default)
    {
        IReadOnlyList<ShelterSummaryDto> nearest = SeedData.DhakaSeedData.Shelters
            .OrderBy(s => HaversineMeters(origin, s.Location))
            .ThenBy(s => s.Id)
            .Take(Math.Max(count, 0))
            .ToList();
        return Task.FromResult(nearest);
    }

    internal static double HaversineMeters(GeoPoint a, GeoPoint b)
    {
        const double earthRadiusMeters = 6_371_000;
        var dLat = ToRadians(b.Latitude - a.Latitude);
        var dLon = ToRadians(b.Longitude - a.Longitude);
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(ToRadians(a.Latitude)) * Math.Cos(ToRadians(b.Latitude))
                * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        // Clamp: fp error can push sqrt(h) past 1.0 for near-antipodal points → Asin NaN.
        return 2 * earthRadiusMeters * Math.Asin(Math.Min(1.0, Math.Sqrt(h)));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
