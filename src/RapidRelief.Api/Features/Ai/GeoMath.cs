using RapidRelief.Shared.Contracts.Common;

namespace RapidRelief.Api.Features.Ai;

/// <summary>
/// Feature-local Haversine copy — Features/Ai must not reference Features/Stubs (§4.1),
/// so the formula is duplicated here (blueprint F8 risk 4).
/// </summary>
internal static class GeoMath
{
    public static double HaversineMeters(GeoPoint a, GeoPoint b)
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
