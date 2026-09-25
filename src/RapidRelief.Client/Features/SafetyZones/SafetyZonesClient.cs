using System.Net.Http.Json;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Client.Features.SafetyZones;

public sealed class SafetyZonesClient(HttpClient http)
{
    // =========================================================================
    // SAFETY ZONES
    // =========================================================================

    public async Task<IReadOnlyList<SafetyZoneDto>> GetZonesAsync(bool all = false, CancellationToken ct = default)
    {
        try
        {
            var uri = all ? "/api/safety/zones?all=true" : "/api/safety/zones";
            var response = await http.GetFromJsonAsync<ApiEnvelope<IReadOnlyList<SafetyZoneDto>>>(uri, ct);
            return response?.Data ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<SafetyZoneDto?> GetZoneAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await http.GetFromJsonAsync<ApiEnvelope<SafetyZoneDto>>($"/api/safety/zones/{id}", ct);
            return response?.Data;
        }
        catch
        {
            return null;
        }
    }

    public async Task<SafetyZoneDto?> CreateZoneAsync(CreateSafetyZoneRequest request, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("/api/safety/zones", request, ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<SafetyZoneDto>>(cancellationToken: ct);
        return envelope?.Data;
    }

    public async Task<SafetyZoneDto?> UpdateZoneAsync(Guid id, UpdateSafetyZoneRequest request, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync($"/api/safety/zones/{id}", request, ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<SafetyZoneDto>>(cancellationToken: ct);
        return envelope?.Data;
    }

    public async Task<bool> DeleteZoneAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync($"/api/safety/zones/{id}", ct);
        return response.IsSuccessStatusCode;
    }

    // =========================================================================
    // ROAD CLOSURES
    // =========================================================================

    public async Task<IReadOnlyList<RoadClosureDto>> GetClosuresAsync(bool all = false, CancellationToken ct = default)
    {
        try
        {
            var uri = all ? "/api/safety/closures?all=true" : "/api/safety/closures";
            var response = await http.GetFromJsonAsync<ApiEnvelope<IReadOnlyList<RoadClosureDto>>>(uri, ct);
            return response?.Data ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<RoadClosureDto?> GetClosureAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var response = await http.GetFromJsonAsync<ApiEnvelope<RoadClosureDto>>($"/api/safety/closures/{id}", ct);
            return response?.Data;
        }
        catch
        {
            return null;
        }
    }

    public async Task<RoadClosureDto?> CreateClosureAsync(CreateRoadClosureRequest request, CancellationToken ct = default)
    {
        var response = await http.PostAsJsonAsync("/api/safety/closures", request, ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<RoadClosureDto>>(cancellationToken: ct);
        return envelope?.Data;
    }

    public async Task<RoadClosureDto?> UpdateClosureAsync(Guid id, UpdateRoadClosureRequest request, CancellationToken ct = default)
    {
        var response = await http.PutAsJsonAsync($"/api/safety/closures/{id}", request, ct);
        if (!response.IsSuccessStatusCode) return null;
        var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<RoadClosureDto>>(cancellationToken: ct);
        return envelope?.Data;
    }

    public async Task<bool> DeleteClosureAsync(Guid id, CancellationToken ct = default)
    {
        var response = await http.DeleteAsync($"/api/safety/closures/{id}", ct);
        return response.IsSuccessStatusCode;
    }
}
