using System.Text.Json;
using RapidRelief.Client.Common.Map;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Client.Features.SafetyZones;

public static class SafetyMapAdapters
{
    public static IEnumerable<MapPlacemark> ToPlacemarks(this IEnumerable<SafetyZoneDto>? zones)
    {
        if (zones is null) yield break;

        foreach (var z in zones)
        {
            var isCritical = z.Severity is Severity.Severe or Severity.Catastrophic;
            var kind = z.ZoneType switch
            {
                ZoneType.SafeAssemblyPoint => MapMarkerKind.SafeAssemblyPoint,
                ZoneType.RestrictedArea => MapMarkerKind.RestrictedArea,
                ZoneType.EvacuationZone => MapMarkerKind.EvacuationZone,
                _ => MapMarkerKind.DangerZone
            };

            yield return new MapPlacemark(
                Key: z.Id.ToString(),
                Location: new GeoPoint(z.CenterLat, z.CenterLng),
                Title: z.Name,
                Detail: z.Description,
                Status: $"{z.ZoneType} ({z.Severity})",
                IsCritical: isCritical,
                Weight: isCritical ? 2d : 1d,
                Kind: kind);
        }
    }

    public static IEnumerable<MapPlacemark> ToPlacemarks(this IEnumerable<RoadClosureDto>? closures)
    {
        if (closures is null) yield break;

        foreach (var c in closures)
        {
            double lat = 23.8103;
            double lng = 90.4125;
            try
            {
                if (!string.IsNullOrWhiteSpace(c.CoordinatesJson) && c.CoordinatesJson != "[]")
                {
                    var parsed = JsonSerializer.Deserialize<double[][]>(c.CoordinatesJson);
                    if (parsed is not null && parsed.Length > 0 && parsed[0].Length >= 2)
                    {
                        lat = parsed[0][0];
                        lng = parsed[0][1];
                    }
                }
            }
            catch
            {
            }

            yield return new MapPlacemark(
                Key: c.Id.ToString(),
                Location: new GeoPoint(lat, lng),
                Title: $"Closed: {c.RoadName}",
                Detail: c.BlockedReason,
                Status: c.Severity.ToString(),
                IsCritical: true,
                Weight: 2d,
                Kind: MapMarkerKind.RoadClosure);
        }
    }
}
