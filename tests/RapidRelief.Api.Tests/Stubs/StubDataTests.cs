using RapidRelief.Api.Features.Stubs;
using RapidRelief.Api.Features.Stubs.SeedData;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Tests.Stubs;

public sealed class StubDataTests
{
    private const double MinLat = 23.6, MaxLat = 24.0, MinLon = 90.2, MaxLon = 90.6;

    [Fact]
    public void Seed_contains_at_least_25_incidents_all_inside_dhaka_bounding_box()
    {
        var incidents = DhakaSeedData.Incidents;

        Assert.True(incidents.Count >= 25, $"Expected >=25 incidents, got {incidents.Count}");
        Assert.All(incidents, i =>
        {
            Assert.InRange(i.Location.Latitude, MinLat, MaxLat);
            Assert.InRange(i.Location.Longitude, MinLon, MaxLon);
        });
    }

    [Fact]
    public void Seed_contains_at_least_three_sos_incidents()
    {
        Assert.True(DhakaSeedData.Incidents.Count(i => i.IsSos) >= 3);
    }

    [Fact]
    public void Seed_contains_the_known_near_duplicate_pair()
    {
        var a = DhakaSeedData.Incidents.SingleOrDefault(i => i.Id == DhakaSeedData.NearDuplicateIncidentIdA);
        var b = DhakaSeedData.Incidents.SingleOrDefault(i => i.Id == DhakaSeedData.NearDuplicateIncidentIdB);

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Equal(a!.Type, b!.Type);
        Assert.True(HaversineMeters(a.Location, b.Location) < 300,
            $"Near-duplicates must be <300m apart, got {HaversineMeters(a.Location, b.Location):F0}m");
        Assert.True((a.ReportedAtUtc - b.ReportedAtUtc).Duration() <= TimeSpan.FromMinutes(30),
            "Near-duplicates must be reported within 30 minutes of each other");
    }

    [Fact]
    public void Seed_incident_timestamps_are_deterministic_and_within_72_hours_before_anchor()
    {
        Assert.All(DhakaSeedData.Incidents, i =>
        {
            Assert.True(i.ReportedAtUtc <= DhakaSeedData.AnchorUtc, $"{i.Id} reported after anchor");
            Assert.True(i.ReportedAtUtc >= DhakaSeedData.AnchorUtc.AddHours(-72), $"{i.Id} older than 72h before anchor");
        });
    }

    [Fact]
    public void Seed_severities_span_full_range_and_statuses_cover_the_state_machine()
    {
        var severities = DhakaSeedData.Incidents.Select(i => i.Severity).Distinct().ToList();
        var statuses = DhakaSeedData.Incidents.Select(i => i.Status).Distinct().ToList();

        Assert.All(Enum.GetValues<Severity>(), s => Assert.Contains(s, severities));
        Assert.All(Enum.GetValues<IncidentStatus>(), s => Assert.Contains(s, statuses));
    }

    [Fact]
    public void Seed_contains_eight_shelters_with_one_full_and_one_closed()
    {
        var shelters = DhakaSeedData.Shelters;

        Assert.Equal(8, shelters.Count);
        Assert.Contains(shelters, s => s.Occupancy == s.Capacity);
        Assert.Contains(shelters, s => !s.IsOpen);
    }

    [Fact]
    public void Seed_registry_and_team_compositions_match_the_blueprint()
    {
        Assert.Equal(6, DhakaSeedData.Hospitals.Count);
        Assert.Equal(10, DhakaSeedData.Volunteers.Count);
        Assert.Equal(5, DhakaSeedData.Ngos.Count);
        Assert.Equal(6, DhakaSeedData.RescueTeams.Count);
    }

    [Fact]
    public async Task GetNearestAsync_returns_shelters_in_ascending_distance_order()
    {
        var service = new FakeShelterReadService();
        var origin = new GeoPoint(23.7925, 90.4078); // Gulshan

        var nearest = await service.GetNearestAsync(origin, count: 5);

        Assert.Equal(5, nearest.Count);
        var distances = nearest.Select(s => HaversineMeters(origin, s.Location)).ToList();
        Assert.Equal(distances.OrderBy(d => d).ToList(), distances);
    }

    [Fact]
    public async Task GetNearestAsync_respects_the_count_argument()
    {
        var service = new FakeShelterReadService();

        var nearest = await service.GetNearestAsync(DhakaSeedData.DhakaCenter, count: 3);

        Assert.Equal(3, nearest.Count);
    }

    [Fact]
    public async Task GetSheltersAsync_returns_all_eight_seeded_shelters()
    {
        var service = new FakeShelterReadService();

        var shelters = await service.GetSheltersAsync();

        Assert.Equal(8, shelters.Count);
    }

    internal static double HaversineMeters(GeoPoint a, GeoPoint b)
    {
        const double earthRadiusMeters = 6_371_000;
        var dLat = ToRadians(b.Latitude - a.Latitude);
        var dLon = ToRadians(b.Longitude - a.Longitude);
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(ToRadians(a.Latitude)) * Math.Cos(ToRadians(b.Latitude))
                * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * earthRadiusMeters * Math.Asin(Math.Sqrt(h));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
