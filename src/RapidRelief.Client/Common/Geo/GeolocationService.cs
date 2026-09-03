using Microsoft.JSInterop;
using RapidRelief.Shared.Contracts.Common;

namespace RapidRelief.Client.Common.Geo;

/// <summary>Why a location lookup produced no fix. <see cref="None"/> means the fix succeeded.</summary>
public enum GeoFailure
{
    None,
    Unsupported,
    Denied,
    Unavailable,
    Timeout,
}

/// <summary>Result of a location request. Never an exception — features branch on <see cref="Failure"/>.</summary>
public sealed record GeoResult(GeoPoint? Point, double AccuracyMeters, GeoFailure Failure)
{
    public bool Ok => Point is not null;

    public string Message => Failure switch
    {
        GeoFailure.Denied => "Location permission was blocked. Enable it in your browser's site settings, or type the address instead.",
        GeoFailure.Unsupported => "This browser cannot share a location. Type the address instead.",
        GeoFailure.Timeout => "Getting a GPS fix took too long. Try again outdoors, or type the address instead.",
        GeoFailure.Unavailable => "Your location is unavailable right now. Try again, or type the address instead.",
        _ => string.Empty,
    };
}

/// <summary>
/// Foundation-owned wrapper over <c>js/geolocation.js</c>. Only prompts when a feature calls it
/// from a user action; failures come back as <see cref="GeoResult"/>, never as exceptions.
/// </summary>
public sealed class GeolocationService : IAsyncDisposable
{
    private const int DefaultTimeoutMs = 10000;

    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;

    public GeolocationService(IJSRuntime js) => _js = js;

    public async Task<GeoResult> GetCurrentAsync(bool highAccuracy = false, int timeoutMs = DefaultTimeoutMs)
    {
        try
        {
            _module ??= await _js.InvokeAsync<IJSObjectReference>("import", "./js/geolocation.js");
            var raw = await _module.InvokeAsync<JsGeoResult>("getCurrentPosition", timeoutMs, highAccuracy);

            return raw is { Ok: true, Lat: not null, Lng: not null }
                ? new GeoResult(new GeoPoint(raw.Lat.Value, raw.Lng.Value), raw.AccuracyMeters, GeoFailure.None)
                : new GeoResult(null, 0, Parse(raw?.Reason));
        }
        catch (JSDisconnectedException)
        {
            return new GeoResult(null, 0, GeoFailure.Unavailable);
        }
        catch (JSException)
        {
            return new GeoResult(null, 0, GeoFailure.Unavailable);
        }
    }

    private static GeoFailure Parse(string? reason) => reason switch
    {
        "denied" => GeoFailure.Denied,
        "unsupported" => GeoFailure.Unsupported,
        "timeout" => GeoFailure.Timeout,
        _ => GeoFailure.Unavailable,
    };

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_module is not null)
            {
                await _module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // Page already torn down.
        }
    }

    private sealed record JsGeoResult(bool Ok, double? Lat, double? Lng, double AccuracyMeters, string? Reason);
}
