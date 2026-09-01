using RapidRelief.Api.Features.Stubs;
using RapidRelief.Shared.Contracts.Common;

namespace RapidRelief.Api.Tests.Stubs;

public sealed class FakeShelterReadServiceTests
{
    [Fact]
    public void Haversine_mirpur_to_gulshan_is_between_5_and_6_km()
    {
        var mirpur = new GeoPoint(23.8223, 90.3654);
        var gulshan = new GeoPoint(23.7925, 90.4078);

        var meters = FakeShelterReadService.HaversineMeters(mirpur, gulshan);

        Assert.InRange(meters, 5_000, 6_000);
    }

    [Fact]
    public void Haversine_never_returns_NaN_even_for_antipodal_points()
    {
        var a = new GeoPoint(23.8103, 90.4125);
        var b = new GeoPoint(-23.8103, -89.5875);

        var meters = FakeShelterReadService.HaversineMeters(a, b);

        Assert.False(double.IsNaN(meters));
        Assert.True(meters > 0);
    }
}
