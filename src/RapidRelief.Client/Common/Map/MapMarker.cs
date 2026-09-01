namespace RapidRelief.Client.Common.Map;

/// <summary>Marker model for <see cref="RapidMap"/>. Kind drives future styling (e.g. "sos").</summary>
public sealed record MapMarker(string Id, double Lat, double Lng, string Title, string Kind);
