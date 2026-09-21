using RapidRelief.Shared.Contracts.Common;

namespace RapidRelief.Client.Common.Map;

/// <summary>
/// The map state a page owns: which layers are on, what is filtered out, where the viewer is, and
/// the markers/heat points that fall out of all of it. Every page used to build its own marker
/// list with its own id scheme, its own title format and its own idea of what "critical" meant;
/// this is the single implementation of those rules.
///
/// Nothing here touches JS or HTTP — a page feeds it placemarks and hands it to <see cref="RapidMap"/>.
/// </summary>
public sealed class MapView
{
    private readonly Dictionary<MapLayerId, List<MapPlacemark>> _items = [];
    private readonly Dictionary<MapLayerId, bool> _visible = [];
    private readonly Dictionary<MapLayerId, string> _labels = [];

    private IReadOnlyList<MapMarker>? _markers;
    private IReadOnlyList<MapHeatPoint>? _heat;
    private IReadOnlyList<MapNearby>? _visibleItems;

    private bool _criticalOnly;
    private double? _radiusKm;
    private string _search = string.Empty;
    private bool _showHeatmap;
    private GeoPoint? _userLocation;

    public MapView(GeoPoint? fallbackCenter = null)
    {
        FallbackCenter = fallbackCenter ?? MapConfigService.DhakaCenter;
    }

    /// <summary>Used when no layer has anything to show — never leave the viewer at 0,0.</summary>
    public GeoPoint FallbackCenter { get; set; }

    /// <summary>Where the viewer is; drives distances, the radius filter and the "you are here" dot.</summary>
    public GeoPoint? UserLocation
    {
        get => _userLocation;
        set => Set(ref _userLocation, value);
    }

    public double UserLocationAccuracyMeters { get; set; }

    /// <summary>Keeps only SOS / severe placemarks in every layer that marks them critical.</summary>
    public bool CriticalOnly
    {
        get => _criticalOnly;
        set => Set(ref _criticalOnly, value);
    }

    /// <summary>Hides anything further than this from <see cref="UserLocation"/>. Ignored without a location.</summary>
    public double? RadiusKm
    {
        get => _radiusKm;
        set => Set(ref _radiusKm, value);
    }

    /// <summary>Case-insensitive match against a placemark's title, detail and status.</summary>
    public string Search
    {
        get => _search;
        set => Set(ref _search, value ?? string.Empty);
    }

    /// <summary>Renders visible placemarks as a weighted concentration layer as well as markers.</summary>
    public bool ShowHeatmap
    {
        get => _showHeatmap;
        set => Set(ref _showHeatmap, value);
    }

    /// <summary>Raised whenever the rendered output would change, so the page can re-render.</summary>
    public event Action? Changed;

    /// <summary>Replaces a layer's contents. Unknown layers are created on first use.</summary>
    public MapView SetLayer(MapLayerId layer, IEnumerable<MapPlacemark>? items, string? label = null)
    {
        _items[layer] = items?.Where(x => x is not null).ToList() ?? [];
        _visible.TryAdd(layer, true);
        if (label is not null)
        {
            _labels[layer] = label;
        }

        Invalidate();
        return this;
    }

    public IReadOnlyList<MapPlacemark> Layer(MapLayerId layer)
        => _items.TryGetValue(layer, out var items) ? items : [];

    public bool IsVisible(MapLayerId layer) => _visible.TryGetValue(layer, out var on) && on;

    public void SetVisible(MapLayerId layer, bool visible)
    {
        if (IsVisible(layer) == visible && _visible.ContainsKey(layer))
        {
            return;
        }

        _visible[layer] = visible;
        Invalidate();
    }

    public void Toggle(MapLayerId layer) => SetVisible(layer, !IsVisible(layer));

    public string LabelFor(MapLayerId layer)
        => _labels.TryGetValue(layer, out var label) ? label : MapLayerDefaults.Label(layer);

    /// <summary>Total in the layer, before filters — the count an operator expects next to a toggle.</summary>
    public int CountIn(MapLayerId layer) => Layer(layer).Count;

    /// <summary>How many of that layer survive the current filters.</summary>
    public int VisibleCountIn(MapLayerId layer)
        => VisibleItems.Count(x => x.Layer == layer);

    public IReadOnlyList<MapLegendEntry> Legend =>
        _items.Keys
            .OrderBy(layer => (int)layer)
            .Select(layer => new MapLegendEntry(
                layer, LabelFor(layer), MapLayerDefaults.Kind(layer), CountIn(layer), IsVisible(layer)))
            .ToList();

    /// <summary>Everything that passes the filters, nearest first when a location is known.</summary>
    public IReadOnlyList<MapNearby> VisibleItems => _visibleItems ??= BuildVisible();

    public IReadOnlyList<MapMarker> Markers => _markers ??= BuildMarkers();

    public IReadOnlyList<MapHeatPoint> HeatPoints => _heat ??= BuildHeat();

    /// <summary>Viewer first, then the middle of what is on screen, then the configured default.</summary>
    public GeoPoint Center
    {
        get
        {
            if (UserLocation is { } here)
            {
                return here;
            }

            var visible = VisibleItems;
            return visible.Count == 0
                ? FallbackCenter
                : new GeoPoint(
                    visible.Average(x => x.Placemark.Location.Latitude),
                    visible.Average(x => x.Placemark.Location.Longitude));
        }
    }

    public double? DistanceKmTo(MapPlacemark placemark)
        => UserLocation is { } here ? MapMath.DistanceKm(here, placemark.Location) : null;

    public string DistanceTextTo(MapPlacemark placemark) => MapMath.FormatDistance(DistanceKmTo(placemark));

    /// <summary>Eight-point bearing from the viewer, or null when we don't know where they are.</summary>
    public string? BearingTo(MapPlacemark placemark)
        => UserLocation is { } here ? MapMath.CompassFrom(here, placemark.Location) : null;

    /// <summary>Turn-by-turn hand-off, pre-filled with the viewer's position when we have it.</summary>
    public string DirectionsUrlTo(MapPlacemark placemark)
        => MapDirections.To(placemark.Location, UserLocation);

    /// <summary>The closest visible placemark, optionally restricted to one layer.</summary>
    public MapNearby? Nearest(MapLayerId? layer = null)
        => VisibleItems.FirstOrDefault(x => layer is null || x.Layer == layer);

    private IReadOnlyList<MapNearby> BuildVisible()
    {
        var search = _search.Trim();
        var results = new List<MapNearby>();

        foreach (var (layer, items) in _items)
        {
            if (!IsVisible(layer))
            {
                continue;
            }

            foreach (var item in items)
            {
                if (_criticalOnly && !item.IsCritical)
                {
                    continue;
                }

                if (search.Length > 0 && !MatchesSearch(item, search))
                {
                    continue;
                }

                var distance = DistanceKmTo(item);
                if (_radiusKm is { } radius && distance is { } km && km > radius)
                {
                    continue;
                }

                results.Add(new MapNearby(item, layer, distance));
            }
        }

        // Nearest first is the order a responder reads; without a fix, keep layer order stable.
        return UserLocation is null
            ? results.OrderBy(x => (int)x.Layer).ThenBy(x => x.Placemark.Title, StringComparer.OrdinalIgnoreCase).ToList()
            : results.OrderBy(x => x.DistanceKm ?? double.MaxValue).ToList();
    }

    private IReadOnlyList<MapMarker> BuildMarkers()
        => VisibleItems
            .Select(x => new MapMarker(
                MarkerId(x.Layer, x.Placemark.Key),
                x.Placemark.Location.Latitude,
                x.Placemark.Location.Longitude,
                MarkerTitle(x),
                x.Placemark.Kind ?? KindFor(x.Layer, x.Placemark)))
            .ToList();

    private IReadOnlyList<MapHeatPoint> BuildHeat()
        => _showHeatmap
            ? VisibleItems
                .Select(x => new MapHeatPoint(
                    x.Placemark.Location.Latitude, x.Placemark.Location.Longitude, Math.Max(0.1, x.Placemark.Weight)))
                .ToList()
            : [];

    /// <summary>Distance belongs in the tooltip: it is the first thing a responder asks.</summary>
    private static string MarkerTitle(MapNearby entry)
    {
        var title = entry.Placemark.Detail is { Length: > 0 } detail
            ? $"{entry.Placemark.Title} — {detail}"
            : entry.Placemark.Title;

        return entry.DistanceKm is null ? title : $"{title} · {entry.DistanceText} away";
    }

    /// <summary>Critical placemarks get the SOS treatment whatever layer they came from.</summary>
    private static string KindFor(MapLayerId layer, MapPlacemark placemark)
        => placemark.IsCritical && layer == MapLayerId.Incidents
            ? MapMarkerKind.Sos
            : MapLayerDefaults.Kind(layer);

    /// <summary>Marker ids are namespaced by layer, so two layers can carry the same entity id.</summary>
    public static string MarkerId(MapLayerId layer, string key)
        => $"{MapLayerDefaults.Prefix(layer)}-{key}";

    private static bool MatchesSearch(MapPlacemark item, string search)
        => item.Title.Contains(search, StringComparison.OrdinalIgnoreCase)
           || (item.Detail?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
           || (item.Status?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);

    private void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        Invalidate();
    }

    private void Invalidate()
    {
        _markers = null;
        _heat = null;
        _visibleItems = null;
        Changed?.Invoke();
    }
}
