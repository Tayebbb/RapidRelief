using RapidRelief.Api.Features.Shelters.Domain;
using RapidRelief.Shared.Contracts.Common;

namespace RapidRelief.Api.Features.Shelters.Services;

public sealed record ShelterSuitability(
    Shelter Shelter,
    double DistanceKm,
    int FreeSpaces,
    double OccupancyRatio,
    double Score,
    IReadOnlyList<string> Reasons);

/// <summary>
/// "Nearest" is the wrong answer when the nearest shelter is full: a citizen sent to a shelter with
/// no space has to walk twice. Suitability blends travel distance with real free capacity and the
/// facilities the shelter offers, and explains the choice in plain language.
/// </summary>
public static class ShelterSuitabilityScorer
{
    private const double EarthRadiusKm = 6371.0;

    /// <summary>Beyond this a shelter is treated as a last resort regardless of how empty it is.</summary>
    private const double MaxUsefulDistanceKm = 15.0;

    public static IReadOnlyList<ShelterSuitability> Rank(
        GeoPoint origin,
        IEnumerable<Shelter> shelters,
        int take)
    {
        var ranked = new List<ShelterSuitability>();

        foreach (var shelter in shelters)
        {
            if (shelter.Status != ShelterStatus.Open)
            {
                continue;
            }

            var free = Math.Max(0, shelter.Capacity - shelter.CurrentOccupancy);
            if (free <= 0)
            {
                continue;
            }

            var distanceKm = DistanceKm(origin, shelter.Location);
            var occupancyRatio = shelter.Capacity <= 0 ? 1 : (double)shelter.CurrentOccupancy / shelter.Capacity;

            // 0-1 each: closer is better, emptier is better, more facilities is better.
            var proximity = Math.Clamp(1 - (distanceKm / MaxUsefulDistanceKm), 0, 1);
            var headroom = Math.Clamp(1 - occupancyRatio, 0, 1);
            var facilities = Math.Clamp(shelter.Facilities.Count / 4.0, 0, 1);

            var score = (proximity * 0.55) + (headroom * 0.35) + (facilities * 0.10);

            var reasons = new List<string>
            {
                $"{distanceKm:F1} km away",
                free == 1 ? "1 space left" : $"{free} spaces left",
            };

            if (occupancyRatio >= 0.85)
            {
                reasons.Add("filling up fast");
            }

            if (shelter.Facilities.Count > 0)
            {
                reasons.Add($"has {string.Join(", ", shelter.Facilities.Take(3)).ToLowerInvariant()}");
            }

            ranked.Add(new ShelterSuitability(shelter, distanceKm, free, occupancyRatio, score, reasons));
        }

        return ranked
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.DistanceKm)
            .Take(Math.Clamp(take, 1, 20))
            .ToList();
    }

    public static double DistanceKm(GeoPoint from, GeoPoint to)
    {
        var dLat = ToRadians(to.Latitude - from.Latitude);
        var dLon = ToRadians(to.Longitude - from.Longitude);
        var a = (Math.Sin(dLat / 2) * Math.Sin(dLat / 2)) +
                (Math.Cos(ToRadians(from.Latitude)) * Math.Cos(ToRadians(to.Latitude)) *
                 Math.Sin(dLon / 2) * Math.Sin(dLon / 2));
        return EarthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
