using RapidRelief.Shared.Contracts.Common;

namespace RapidRelief.Client.Common.Map;

/// <summary>The layers the system knows about. Adding one here makes it filterable everywhere.</summary>
public enum MapLayerId
{
    Incidents,
    Teams,
    Shelters,
    Relief,

    /// <summary>Ad-hoc points a page owns — a report pin, a search result, a destination.</summary>
    Pins,
}

/// <summary>
/// The neutral shape every feature converts its rows into before they reach the map. Feature DTOs
/// live in feature folders and the map lives in Common, so this record is the seam: features own a
/// small adapter, and all the marker/filter/heat/distance rules stay in one place.
/// </summary>
/// <param name="Key">Stable within its layer — the map diffs on it, so it must not change per render.</param>
/// <param name="Weight">Relative importance for the heat layer.</param>
public sealed record MapPlacemark(
    string Key,
    GeoPoint Location,
    string Title,
    string? Detail = null,
    string? Status = null,
    bool IsCritical = false,
    double Weight = 1d,
    string? Kind = null);

/// <summary>One line of the shared legend: the same colour means the same thing on every page.</summary>
public sealed record MapLegendEntry(MapLayerId Layer, string Label, string Kind, int Count, bool Visible);

/// <summary>A placemark paired with how far it is from the viewer, ready to render in a list.</summary>
public sealed record MapNearby(MapPlacemark Placemark, MapLayerId Layer, double? DistanceKm)
{
    public string DistanceText => MapMath.FormatDistance(DistanceKm);
}

internal static class MapLayerDefaults
{
    internal static string Label(MapLayerId layer) => layer switch
    {
        MapLayerId.Incidents => "Incidents",
        MapLayerId.Teams => "Rescue teams",
        MapLayerId.Shelters => "Shelters",
        MapLayerId.Relief => "Relief drop-offs",
        _ => "Pins",
    };

    internal static string Kind(MapLayerId layer) => layer switch
    {
        MapLayerId.Incidents => MapMarkerKind.Incident,
        MapLayerId.Teams => MapMarkerKind.Team,
        MapLayerId.Shelters => MapMarkerKind.Shelter,
        MapLayerId.Relief => MapMarkerKind.Relief,
        _ => MapMarkerKind.Pin,
    };

    /// <summary>Marker ids are namespaced by layer so two layers can share an entity id safely.</summary>
    internal static string Prefix(MapLayerId layer) => layer switch
    {
        MapLayerId.Incidents => "i",
        MapLayerId.Teams => "t",
        MapLayerId.Shelters => "s",
        MapLayerId.Relief => "r",
        _ => "p",
    };
}
