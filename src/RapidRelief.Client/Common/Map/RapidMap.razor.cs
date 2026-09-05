using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using RapidRelief.Shared.Contracts.Common;

namespace RapidRelief.Client.Common.Map;

/// <summary>
/// Foundation-owned Leaflet wrapper (plan §8.8): init/setView/upsert-diff/remove/click-to-pin/
/// heat layer/fit-to-markers/dispose. Features consume this component only — never rapidMap.js
/// internals. Tile settings come from the server, and a tile outage degrades to a marker-only
/// map with a visible notice rather than a blank rectangle.
/// </summary>
public sealed partial class RapidMap : ComponentBase, IAsyncDisposable
{
    [Inject] private IJSRuntime Js { get; set; } = default!;
    [Inject] private ILogger<RapidMap> Logger { get; set; } = default!;
    [Inject] private MapConfigService MapConfig { get; set; } = default!;

    [Parameter] public GeoPoint InitialCenter { get; set; } = new(23.8103, 90.4125);
    [Parameter] public int InitialZoom { get; set; } = 12;
    [Parameter] public IReadOnlyList<MapMarker> Markers { get; set; } = [];
    [Parameter] public EventCallback<GeoPoint> OnMapClick { get; set; }

    /// <summary>
    /// The shared map state (layers, filters, viewer position). When set it supplies the markers,
    /// the heat layer and the "you are here" dot, and re-renders itself when any of them change —
    /// pages should prefer this over wiring the three parameters separately.
    /// </summary>
    [Parameter] public MapView? View { get; set; }

    /// <summary>Weighted concentration overlay. Empty clears it.</summary>
    [Parameter] public IReadOnlyList<MapHeatPoint> HeatPoints { get; set; } = [];

    /// <summary>Frames all current markers after each marker change.</summary>
    [Parameter] public bool FitToMarkers { get; set; }

    /// <summary>"You are here" layer — rendered separately from Markers so features can't remove it.</summary>
    [Parameter] public GeoPoint? UserLocation { get; set; }
    [Parameter] public double UserLocationAccuracyMeters { get; set; }

    private IReadOnlyList<MapMarker> EffectiveMarkers => View?.Markers ?? Markers;

    private IReadOnlyList<MapHeatPoint> EffectiveHeat => View?.HeatPoints ?? HeatPoints;

    private GeoPoint? EffectiveUserLocation => View?.UserLocation ?? UserLocation;

    private double EffectiveAccuracy =>
        View is not null ? View.UserLocationAccuracyMeters : UserLocationAccuracyMeters;

    internal string ElementId { get; } = $"rapid-map-{Guid.NewGuid():N}";

    /// <summary>True once the tile provider has failed repeatedly; markers keep working.</summary>
    private bool _tilesUnavailable;

    private IJSObjectReference? _module;
    private DotNetObjectReference<RapidMap>? _selfRef;
    private MapView? _boundView;
    private Dictionary<string, MapMarker> _renderedMarkers = new();
    private IReadOnlyList<MapHeatPoint> _renderedHeat = [];
    private GeoPoint? _renderedUserLocation;
    private double _renderedUserAccuracy;
    private bool _disposed;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _disposed)
        {
            return;
        }

        IJSObjectReference? module = null;
        try
        {
            var tiles = await MapConfig.GetAsync();
            module = await Js.InvokeAsync<IJSObjectReference>("import", "./js/rapidMap.js");
            if (_disposed)
            {
                // Disposed mid-init: tear down what was just created and bail.
                await module.DisposeAsync();
                return;
            }

            _selfRef = DotNetObjectReference.Create(this);
            await module.InvokeVoidAsync("init", ElementId, _selfRef,
                InitialCenter.Latitude, InitialCenter.Longitude, InitialZoom,
                new { tileUrl = tiles.TileUrl, attribution = tiles.Attribution, maxZoom = tiles.MaxZoom });
            if (_disposed)
            {
                await module.InvokeVoidAsync("dispose", ElementId);
                await module.DisposeAsync();
                return;
            }

            _module = module;
            await SyncMarkersAsync();
            await SyncHeatAsync();
            await SyncUserLocationAsync();
        }
        catch (JSException ex)
        {
            // A broken map (missing Leaflet asset, JS error) must never crash the page.
            Logger.LogError(ex, "RapidMap initialization failed for element {ElementId}", ElementId);
            if (_module is null && module is not null)
            {
                await DisposeModuleQuietlyAsync(module);
            }
        }
        catch (JSDisconnectedException)
        {
            // Page torn down mid-init — nothing to clean up on the JS side.
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        BindView();
        if (_module is not null)
        {
            await SyncMarkersAsync();
            await SyncHeatAsync();
            await SyncUserLocationAsync();
        }
    }

    /// <summary>The view mutates outside the render loop (a filter toggle, a fresh poll) — follow it.</summary>
    private void BindView()
    {
        if (ReferenceEquals(_boundView, View))
        {
            return;
        }

        if (_boundView is not null)
        {
            _boundView.Changed -= OnViewChanged;
        }

        _boundView = View;
        if (_boundView is not null)
        {
            _boundView.Changed += OnViewChanged;
        }
    }

    private void OnViewChanged() => _ = InvokeAsync(async () =>
    {
        if (_module is null || _disposed)
        {
            return;
        }

        await SyncMarkersAsync();
        await SyncHeatAsync();
        await SyncUserLocationAsync();
        StateHasChanged();
    });

    /// <summary>Diffs against the last rendered set: upserts current ids, removes vanished ones.</summary>
    private async Task SyncMarkersAsync()
    {
        if (_module is null || _disposed)
        {
            return;
        }

        // Duplicate marker ids must not throw — last one wins.
        var current = EffectiveMarkers.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.Last());

        var removedIds = _renderedMarkers.Keys.Where(id => !current.ContainsKey(id)).ToArray();
        if (removedIds.Length > 0)
        {
            await _module.InvokeVoidAsync("removeMarkers", ElementId, removedIds);
        }

        var upserts = current.Values
            .Where(m => !_renderedMarkers.TryGetValue(m.Id, out var previous) || previous != m)
            .ToArray();
        if (upserts.Length > 0)
        {
            await _module.InvokeVoidAsync("upsertMarkers", ElementId, upserts);
        }

        var changed = removedIds.Length > 0 || upserts.Length > 0;
        _renderedMarkers = current;

        if (changed && FitToMarkers && current.Count > 0)
        {
            await _module.InvokeVoidAsync("fitToMarkers", ElementId, 32);
        }
    }

    private async Task SyncHeatAsync()
    {
        if (_module is null || _disposed || _renderedHeat.SequenceEqual(EffectiveHeat))
        {
            return;
        }

        _renderedHeat = EffectiveHeat.ToList();
        if (_renderedHeat.Count == 0)
        {
            await _module.InvokeVoidAsync("clearHeatmap", ElementId);
            return;
        }

        await _module.InvokeVoidAsync("setHeatmap", ElementId,
            _renderedHeat.Select(p => new { lat = p.Lat, lng = p.Lng, weight = p.Weight }).ToArray());
    }

    private async Task SyncUserLocationAsync()
    {
        if (_module is null || _disposed)
        {
            return;
        }

        var location = EffectiveUserLocation;
        var accuracy = EffectiveAccuracy;

        if (location is null)
        {
            if (_renderedUserLocation is not null)
            {
                await _module.InvokeVoidAsync("clearUserLocation", ElementId);
                _renderedUserLocation = null;
            }
            return;
        }

        if (_renderedUserLocation != location || _renderedUserAccuracy != accuracy)
        {
            await _module.InvokeVoidAsync("setUserLocation", ElementId,
                location.Latitude, location.Longitude, accuracy);
            _renderedUserLocation = location;
            _renderedUserAccuracy = accuracy;
        }
    }

    public async Task SetViewAsync(GeoPoint center, int zoom)
    {
        if (_module is not null && !_disposed)
        {
            await _module.InvokeVoidAsync("setView", ElementId, center.Latitude, center.Longitude, zoom);
        }
    }

    [JSInvokable]
    public Task OnMapClicked(double lat, double lng) => OnMapClick.InvokeAsync(new GeoPoint(lat, lng));

    /// <summary>Raised once after repeated tile failures so the page can say the basemap is down.</summary>
    [JSInvokable]
    public Task OnTilesUnavailable()
    {
        if (_tilesUnavailable || _disposed)
        {
            return Task.CompletedTask;
        }

        _tilesUnavailable = true;
        Logger.LogWarning("Map tiles are unavailable for {ElementId} — rendering markers without a basemap", ElementId);
        return InvokeAsync(StateHasChanged);
    }

    public async ValueTask DisposeAsync()
    {
        // Set FIRST so an in-flight OnAfterRenderAsync sees it after each await.
        _disposed = true;
        if (_boundView is not null)
        {
            _boundView.Changed -= OnViewChanged;
            _boundView = null;
        }

        try
        {
            if (_module is not null)
            {
                await _module.InvokeVoidAsync("dispose", ElementId);
                await _module.DisposeAsync();
            }
        }
        catch (JSException)
        {
            // The page is going away; a failed teardown is not worth surfacing.
        }
        catch (JSDisconnectedException)
        {
            // Circuit already gone — nothing to dispose on the JS side.
        }
        finally
        {
            _module = null;
            _selfRef?.Dispose();
            _selfRef = null;
        }
    }

    private async Task DisposeModuleQuietlyAsync(IJSObjectReference module)
    {
        try
        {
            await module.DisposeAsync();
        }
        catch (JSException)
        {
            // Best effort — the import already failed.
        }
        catch (JSDisconnectedException)
        {
            // Circuit gone.
        }
    }
}
