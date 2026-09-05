using RapidRelief.Client.Common.Map;

namespace RapidRelief.Client.Features.Command;

/// <summary>Board rows onto the shared map. The rules themselves live in <see cref="SharedMapAdapters"/>.</summary>
public static class CommandMapAdapters
{
    public static MapPlacemark ToPlacemark(this IncidentSearchRow incident) => new(
        incident.Id.ToString("N"),
        incident.Location,
        incident.Title,
        Detail: incident.IsSos ? "SOS" : incident.Severity.ToString(),
        Status: incident.Status.ToString(),
        IsCritical: SharedMapAdapters.IsCritical(incident.IsSos, incident.Severity),
        Weight: SharedMapAdapters.Weight(incident.IsSos, incident.Severity, incident.PriorityScore));

    public static IEnumerable<MapPlacemark> ToPlacemarks(this IEnumerable<IncidentSearchRow> incidents)
        => incidents.Select(ToPlacemark);
}
