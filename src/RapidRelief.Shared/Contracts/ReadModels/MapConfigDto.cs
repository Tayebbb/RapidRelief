namespace RapidRelief.Shared.Contracts.ReadModels;

/// <summary>
/// Tile provider settings resolved server-side. The URL template is assembled from configuration
/// (never from source), so switching to a keyed provider is a deployment change, not a code change.
/// A browser-rendered map necessarily exposes whatever key ends up in the tile URL — keep tile keys
/// domain-restricted at the provider, and never reuse a server-side key here.
/// </summary>
public sealed record MapConfigDto(
    string TileUrl,
    string Attribution,
    int MaxZoom,
    GeoPointDto DefaultCenter,
    int DefaultZoom)
{
    public static MapConfigDto Fallback { get; } = new(
        "https://tile.openstreetmap.org/{z}/{x}/{y}.png",
        "© OpenStreetMap contributors",
        19,
        new GeoPointDto(23.8103, 90.4125),
        12);
}

/// <summary>Plain lat/lng pair for wire payloads that must not depend on the GeoPoint value type.</summary>
public sealed record GeoPointDto(double Latitude, double Longitude);
