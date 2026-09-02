using RapidRelief.Api.Features.Ai;
using RapidRelief.Shared.Contracts.Common;

namespace RapidRelief.Api.Tests.Ai;

/// <summary>Feature-local Haversine copy (blueprint risk 4) — pinned against known Dhaka distances.</summary>
public sealed class GeoMathTests
{
    [Fact]
    public void Seeded_near_duplicate_pair_is_roughly_130_meters_apart()
    {
        // Coordinates of DhakaSeedData incidents 5 and 6 (the intentional near-duplicate pair).
        var a = new GeoPoint(23.8225, 90.3652);
        var b = new GeoPoint(23.8235, 90.3660);

        Assert.InRange(GeoMath.HaversineMeters(a, b), 115, 150);
    }

    [Fact]
    public void Mirpur_to_Gulshan_is_roughly_five_kilometers()
    {
        var mirpur = new GeoPoint(23.8223, 90.3654);
        var gulshan = new GeoPoint(23.7925, 90.4078);

        Assert.InRange(GeoMath.HaversineMeters(mirpur, gulshan), 4900, 5500);
    }

    [Fact]
    public void Identical_points_are_zero_meters_apart()
    {
        var point = new GeoPoint(23.8103, 90.4125);

        Assert.Equal(0, GeoMath.HaversineMeters(point, point));
    }

    [Fact]
    public void Distance_is_symmetric()
    {
        var a = new GeoPoint(23.8225, 90.3652);
        var b = new GeoPoint(23.7101, 90.3720);

        Assert.Equal(GeoMath.HaversineMeters(a, b), GeoMath.HaversineMeters(b, a), precision: 6);
    }
}
