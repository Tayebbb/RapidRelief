using Microsoft.AspNetCore.Components;
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

    [Parameter] public GeoPoint Center { get; set; } = new(23.8103, 90.4125);
    [Parameter] public int Zoom { get; set; } = 12;
    [Parameter] public IReadOnlyList<MapMarker> Markers { get; set; } = [];
    [Parameter] public EventCallback<GeoPoint> OnMapClick { get; set; }

    internal string ElementId { get; } = $"rapid-map-{Guid.NewGuid():N}";

    private IJSObjectReference? _module;
    private DotNetObjectReference<RapidMap>? _selfRef;
    private Dictionary<string, MapMarker> _renderedMarkers = new();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _module = await Js.InvokeAsync<IJSObjectReference>("import", "./js/rapidMap.js");
            _selfRef = DotNetObjectReference.Create(this);
            await _module.InvokeVoidAsync("init", ElementId, _selfRef, Center.Latitude, Center.Longitude, Zoom);
            await SyncMarkersAsync();
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        if (_module is not null)
        {
            await SyncMarkersAsync();
        }
    }

    /// <summary>Diffs against the last rendered set: upserts current ids, removes vanished ones.</summary>
    private async Task SyncMarkersAsync()
    {
        if (_module is null)
        {
            return;
        }

        var current = Markers.ToDictionary(m => m.Id);

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

    public async Task SetViewAsync(GeoPoint center, int zoom)
    {
        if (_module is not null)
        {
            await _module.InvokeVoidAsync("setView", ElementId, center.Latitude, center.Longitude, zoom);
        }
    }

    [JSInvokable]
    public Task OnMapClicked(double lat, double lng) => OnMapClick.InvokeAsync(new GeoPoint(lat, lng));

    public async ValueTask DisposeAsync()
    {
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
}
