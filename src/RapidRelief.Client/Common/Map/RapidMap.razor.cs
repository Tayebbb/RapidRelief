using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using RapidRelief.Shared.Contracts.Common;

namespace RapidRelief.Client.Common.Map;

/// <summary>
/// Foundation-owned Leaflet wrapper (plan §8.8): init/setView/upsert-diff/remove/click-to-pin/
/// dispose. Features consume this component only — never rapidMap.js internals.
/// </summary>
public sealed partial class RapidMap : ComponentBase, IAsyncDisposable
{
    [Inject] private IJSRuntime Js { get; set; } = default!;
    [Inject] private ILogger<RapidMap> Logger { get; set; } = default!;

    [Parameter] public GeoPoint InitialCenter { get; set; } = new(23.8103, 90.4125);
    [Parameter] public int InitialZoom { get; set; } = 12;
    [Parameter] public IReadOnlyList<MapMarker> Markers { get; set; } = [];
    [Parameter] public EventCallback<GeoPoint> OnMapClick { get; set; }

    /// <summary>"You are here" layer — rendered separately from Markers so features can't remove it.</summary>
    [Parameter] public GeoPoint? UserLocation { get; set; }
    [Parameter] public double UserLocationAccuracyMeters { get; set; }

    internal string ElementId { get; } = $"rapid-map-{Guid.NewGuid():N}";

    private IJSObjectReference? _module;
    private DotNetObjectReference<RapidMap>? _selfRef;
    private Dictionary<string, MapMarker> _renderedMarkers = new();
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
            module = await Js.InvokeAsync<IJSObjectReference>("import", "./js/rapidMap.js");
            if (_disposed)
            {
                // Disposed mid-init: tear down what was just created and bail.
                await module.DisposeAsync();
                return;
            }

            _selfRef = DotNetObjectReference.Create(this);
            await module.InvokeVoidAsync("init", ElementId, _selfRef, InitialCenter.Latitude, InitialCenter.Longitude, InitialZoom);
            if (_disposed)
            {
                await module.InvokeVoidAsync("dispose", ElementId);
                await module.DisposeAsync();
                return;
            }

            _module = module;
            await SyncMarkersAsync();
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
        if (_module is not null)
        {
            await SyncMarkersAsync();
            await SyncUserLocationAsync();
        }
    }

    /// <summary>Diffs against the last rendered set: upserts current ids, removes vanished ones.</summary>
    private async Task SyncMarkersAsync()
    {
        if (_module is null || _disposed)
        {
            return;
        }

        // Duplicate marker ids must not throw — last one wins.
        var current = Markers.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.Last());

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

        _renderedMarkers = current;
    }

    private async Task SyncUserLocationAsync()
    {
        if (_module is null || _disposed)
        {
            return;
        }

        if (UserLocation is null)
        {
            if (_renderedUserLocation is not null)
            {
                await _module.InvokeVoidAsync("clearUserLocation", ElementId);
                _renderedUserLocation = null;
            }
            return;
        }

        if (_renderedUserLocation != UserLocation || _renderedUserAccuracy != UserLocationAccuracyMeters)
        {
            await _module.InvokeVoidAsync("setUserLocation", ElementId,
                UserLocation.Latitude, UserLocation.Longitude, UserLocationAccuracyMeters);
            _renderedUserLocation = UserLocation;
            _renderedUserAccuracy = UserLocationAccuracyMeters;
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

    public async ValueTask DisposeAsync()
    {
        // Set FIRST so an in-flight OnAfterRenderAsync sees it after each await.
        _disposed = true;
        try
        {
            if (_module is not null)
            {
                await _module.InvokeVoidAsync("dispose", ElementId);
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // Circuit/page already gone — nothing to clean up on the JS side.
        }
        finally
        {
            _selfRef?.Dispose();
        }
    }

    private static async ValueTask DisposeModuleQuietlyAsync(IJSObjectReference module)
    {
        try
        {
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }
    }
}
