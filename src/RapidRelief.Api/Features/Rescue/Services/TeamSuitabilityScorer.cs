using RapidRelief.Api.Features.Rescue.Domain;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Api.Features.Rescue.Services;

public sealed record TeamSuitability(RescueTeam Team, double? DistanceKm, int ActiveMissions, double Score, IReadOnlyList<string> Reasons);

/// <summary>
/// Picking a team is not "whoever is closest": a team already running a rescue, or one that is off
/// duty, cannot help. Suitability weighs availability first, then proximity, then current load and
/// speciality match, and explains itself so a dispatcher can override it.
/// </summary>
public static class TeamSuitabilityScorer
{
    private const double MaxUsefulDistanceKm = 25.0;

    public static IReadOnlyList<TeamSuitability> Rank(
        GeoPoint incidentLocation,
        DisasterType incidentType,
        IEnumerable<RescueTeam> teams,
        IReadOnlyDictionary<Guid, int> activeMissionsByTeam)
    {
        var ranked = new List<TeamSuitability>();

        foreach (var team in teams)
        {
            if (string.Equals(team.Status, TeamStatus.OffDuty, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var active = activeMissionsByTeam.TryGetValue(team.Id, out var count) ? count : 0;
            var distanceKm = team.CurrentLatitude is { } lat && team.CurrentLongitude is { } lng
                ? Haversine(incidentLocation, new GeoPoint(lat, lng))
                : (double?)null;

            var availability = active == 0 ? 1.0 : 0.0;
            var proximity = distanceKm is { } km ? Math.Clamp(1 - (km / MaxUsefulDistanceKm), 0, 1) : 0.4;
            var load = Math.Clamp(1 - (active / 3.0), 0, 1);
            var speciality = Matches(team.Specialization, incidentType) ? 1.0 : 0.0;

            var score = (availability * 0.45) + (proximity * 0.30) + (load * 0.15) + (speciality * 0.10);

            var reasons = new List<string>
            {
                active == 0 ? "free now" : $"{active} active mission{(active == 1 ? "" : "s")}",
            };

            if (distanceKm is { } d)
            {
                reasons.Add($"{d:F1} km from the scene");
            }
            else
            {
                reasons.Add("position unknown");
            }

            if (speciality > 0)
            {
                reasons.Add($"specialises in {team.Specialization.ToLowerInvariant()}");
            }

            ranked.Add(new TeamSuitability(team, distanceKm, active, score, reasons));
        }

        return ranked
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.DistanceKm ?? double.MaxValue)
            .ToList();
    }

    private static bool Matches(string specialization, DisasterType type) =>
        specialization.Contains(type.ToString(), StringComparison.OrdinalIgnoreCase) ||
        (type == DisasterType.Flood && specialization.Contains("water", StringComparison.OrdinalIgnoreCase)) ||
        (type == DisasterType.BuildingCollapse && specialization.Contains("urban", StringComparison.OrdinalIgnoreCase)) ||
        (type == DisasterType.Fire && specialization.Contains("fire", StringComparison.OrdinalIgnoreCase));

    public static double Haversine(GeoPoint from, GeoPoint to)
    {
        const double earthRadiusKm = 6371.0;
        var dLat = ToRadians(to.Latitude - from.Latitude);
        var dLon = ToRadians(to.Longitude - from.Longitude);
        var a = (Math.Sin(dLat / 2) * Math.Sin(dLat / 2)) +
                (Math.Cos(ToRadians(from.Latitude)) * Math.Cos(ToRadians(to.Latitude)) *
                 Math.Sin(dLon / 2) * Math.Sin(dLon / 2));
        return earthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;

    /// <summary>Operational severity bands used by the dashboard.</summary>
    public static string Band(IncidentSummaryDto incident) => incident switch
    {
        { IsSos: true } => "Critical",
        { Severity: Severity.Catastrophic } => "Critical",
        { Severity: Severity.Severe } => "High",
        { Severity: Severity.Moderate } => "Medium",
        _ => "Low",
    };
}
