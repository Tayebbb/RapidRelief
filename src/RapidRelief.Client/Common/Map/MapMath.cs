using System.Globalization;
using RapidRelief.Shared.Contracts.Common;

namespace RapidRelief.Client.Common.Map;

/// <summary>
/// The one place distance and direction maths lives on the client. Features must not re-derive
/// Haversine inline — a second copy is a second set of rounding rules in front of an operator.
/// </summary>
public static class MapMath
{
    private const double EarthRadiusKm = 6371.0;

    public static double DistanceKm(GeoPoint from, GeoPoint to)
    {
        var dLat = ToRadians(to.Latitude - from.Latitude);
        var dLng = ToRadians(to.Longitude - from.Longitude);
        var a = (Math.Sin(dLat / 2) * Math.Sin(dLat / 2))
            + (Math.Cos(ToRadians(from.Latitude)) * Math.Cos(ToRadians(to.Latitude))
               * Math.Sin(dLng / 2) * Math.Sin(dLng / 2));
        return EarthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    /// <summary>Metres under a kilometre, one decimal above it — the precision a responder can act on.</summary>
    public static string FormatDistance(double? kilometres) => kilometres switch
    {
        null => "—",
        < 0.1 => "under 100 m",
        < 1 => $"{kilometres.Value * 1000:F0} m",
        < 10 => $"{kilometres.Value:F1} km",
        _ => $"{kilometres.Value:F0} km",
    };

    public static string FormatCoordinate(double value) => value.ToString("F4", CultureInfo.InvariantCulture);

    public static string FormatPoint(GeoPoint point)
        => $"{FormatCoordinate(point.Latitude)}, {FormatCoordinate(point.Longitude)}";

    /// <summary>Compass bearing from one point to another, as an eight-point label.</summary>
    public static string CompassFrom(GeoPoint from, GeoPoint to)
    {
        var dLng = ToRadians(to.Longitude - from.Longitude);
        var lat1 = ToRadians(from.Latitude);
        var lat2 = ToRadians(to.Latitude);
        var y = Math.Sin(dLng) * Math.Cos(lat2);
        var x = (Math.Cos(lat1) * Math.Sin(lat2)) - (Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLng));
        var degrees = (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;

        return degrees switch
        {
            < 22.5 or >= 337.5 => "north",
            < 67.5 => "north-east",
            < 112.5 => "east",
            < 157.5 => "south-east",
            < 202.5 => "south",
            < 247.5 => "south-west",
            < 292.5 => "west",
            _ => "north-west",
        };
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}

/// <summary>
/// Hand-off to whatever navigation app the responder actually uses. Centralised because three
/// pages previously built this URL by hand — and one of them would eventually get it wrong.
/// </summary>
public static class MapDirections
{
    /// <summary>Universal Google Maps directions URL; works on web, Android and iOS.</summary>
    public static string To(GeoPoint destination, GeoPoint? origin = null)
    {
        var url = "https://www.google.com/maps/dir/?api=1&destination="
            + Format(destination.Latitude) + "," + Format(destination.Longitude);
        return origin is { } from
            ? url + "&origin=" + Format(from.Latitude) + "," + Format(from.Longitude)
            : url;
    }

    /// <summary>OpenStreetMap fallback for environments where Google is unreachable.</summary>
    public static string OpenStreetMap(GeoPoint destination, GeoPoint? origin = null)
    {
        var to = Format(destination.Latitude) + "," + Format(destination.Longitude);
        var from = origin is { } start
            ? Format(start.Latitude) + "," + Format(start.Longitude)
            : string.Empty;
        return $"https://www.openstreetmap.org/directions?from={from}&to={to}";
    }

    private static string Format(double value) => value.ToString("F6", CultureInfo.InvariantCulture);
}
