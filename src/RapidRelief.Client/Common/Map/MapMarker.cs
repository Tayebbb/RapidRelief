namespace RapidRelief.Client.Common.Map;

/// <summary>
/// Marker model for <see cref="RapidMap"/>. <paramref name="Kind"/> drives the shared icon
/// styling — features choose a kind, never a colour, so the legend means the same thing on
/// every page.
/// </summary>
public sealed record MapMarker(string Id, double Lat, double Lng, string Title, string Kind);

/// <summary>Kinds the shared map knows how to style. Anything else falls back to a neutral pin.</summary>
public static class MapMarkerKind
{
    public const string Sos = "sos";
    public const string Incident = "incident";
    public const string Team = "team";
    public const string Shelter = "shelter";
    public const string Relief = "relief";
    public const string Pin = "pin";
}

/// <summary>One weighted contribution to the heat layer; weight is relative within a render.</summary>
public sealed record MapHeatPoint(double Lat, double Lng, double Weight);
