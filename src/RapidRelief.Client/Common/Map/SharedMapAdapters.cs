using RapidRelief.Client.Common.Map;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Client.Common.Map;

/// <summary>
/// Converts the shared read models into map placemarks. Feature-owned DTOs get their own adapter
/// next to the DTO; these two live in Shared, so their adapter lives with the map.
/// </summary>
public static class SharedMapAdapters
{
    /// <summary>SOS and the top two severities are what "critical" means anywhere in the system.</summary>
    public static bool IsCritical(bool isSos, Severity severity)
        => isSos || severity >= Severity.Severe;

    /// <summary>
    /// Heat weight for an incident. The AI priority score leads when triage has run; before that
    /// the reported severity stands in, so a fresh SOS is hot immediately rather than invisible.
    /// </summary>
    public static double Weight(bool isSos, Severity severity, double? priorityScore)
    {
        if (priorityScore is { } score and > 0)
        {
            return Math.Clamp(score, 0.1, 10d);
        }

        var baseline = severity switch
        {
            Severity.Catastrophic => 4d,
            Severity.Severe => 3d,
            Severity.Moderate => 2d,
            _ => 1d,
        };

        return isSos ? baseline + 2d : baseline;
    }

    public static MapPlacemark ToPlacemark(this IncidentSummaryDto incident) => new(
        incident.Id.ToString("N"),
        incident.Location,
        incident.Summary is { Length: > 0 } summary ? summary : incident.Type.ToString(),
        Detail: incident.IsSos ? "SOS" : incident.Severity.ToString(),
        Status: incident.Status.ToString(),
        IsCritical: IsCritical(incident.IsSos, incident.Severity),
        Weight: Weight(incident.IsSos, incident.Severity, incident.PriorityScore));

    public static MapPlacemark ToPlacemark(this ShelterSummaryDto shelter)
    {
        var free = Math.Max(0, shelter.Capacity - shelter.Occupancy);
        return new MapPlacemark(
            shelter.Id.ToString("N"),
            shelter.Location,
            shelter.Name,
            Detail: shelter.IsOpen ? $"{free} of {shelter.Capacity} free" : "Closed",
            Status: shelter.IsOpen ? "Open" : "Closed",
            // A shelter with no space left is the one an operator must not miss.
            IsCritical: shelter.IsOpen && free == 0,
            Weight: Math.Max(1, free));
    }

    public static IEnumerable<MapPlacemark> ToPlacemarks(this IEnumerable<IncidentSummaryDto> incidents)
        => incidents.Select(ToPlacemark);

    public static IEnumerable<MapPlacemark> ToPlacemarks(this IEnumerable<ShelterSummaryDto> shelters)
        => shelters.Select(ToPlacemark);
}
