using System.Net.Http.Json;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Client.Common.Map;

/// <summary>
/// The system's entry point to the map: tile settings (fetched once per session, never from
/// client source — the provider key is substituted server-side) and the factory for a
/// <see cref="MapView"/> that starts centred where the deployment says it should.
///
/// A failed fetch is not an error state. The map falls back to the OpenStreetMap defaults so a
/// config outage, an offline start or a 500 never leaves a blank rectangle on screen.
/// </summary>
public sealed class MapConfigService(HttpClient http)
{
    public static readonly GeoPoint DhakaCenter = new(23.8103, 90.4125);

    private MapConfigDto? _cached;
    private Task<MapConfigDto>? _inFlight;

    public MapConfigDto Current => _cached ?? MapConfigDto.Fallback;

    /// <summary>True once a fetch has settled, whether it succeeded or fell back.</summary>
    public bool Loaded { get; private set; }

    /// <summary>True when the server could not be reached and the OSM defaults are in use.</summary>
    public bool UsingFallback { get; private set; }

    public GeoPoint DefaultCenter =>
        new(Current.DefaultCenter.Latitude, Current.DefaultCenter.Longitude);

    public int DefaultZoom => Current.DefaultZoom;

    /// <summary>
    /// A map for one page, pre-centred on the configured default. Pages call this instead of
    /// building marker lists by hand, which is what kept every page's map subtly different.
    /// </summary>
    public MapView CreateView() => new(DefaultCenter);

    public Task<MapConfigDto> GetAsync(CancellationToken ct = default)
    {
        if (_cached is not null)
        {
            return Task.FromResult(_cached);
        }

        // Single-flight: eight map components mounting at once must not make eight requests.
        return _inFlight ??= LoadAsync(ct);
    }

    private async Task<MapConfigDto> LoadAsync(CancellationToken ct)
    {
        try
        {
            var envelope = await http.GetFromJsonAsync<ApiEnvelope<MapConfigDto>>("api/foundation/map-config", ct);
            _cached = envelope?.Data ?? MapConfigDto.Fallback;
            UsingFallback = envelope?.Data is null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
                                       or NotSupportedException or System.Text.Json.JsonException)
        {
            _cached = MapConfigDto.Fallback;
            UsingFallback = true;
        }
        finally
        {
            Loaded = true;
            _inFlight = null;
        }

        return _cached!;
    }
}
