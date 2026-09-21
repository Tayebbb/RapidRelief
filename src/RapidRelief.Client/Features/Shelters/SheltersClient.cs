using System.Globalization;
using System.Net.Http.Json;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.ReadModels;
namespace RapidRelief.Client.Features.Shelters;

public sealed class SheltersClient(HttpClient http)
{
    public async Task<ApiEnvelope<PagedResult<ShelterSummaryDto>>?> GetSheltersAsync(double? lat = null, double? lng = null, CancellationToken ct = default)
    {
        var url = "api/shelters";
        if (lat.HasValue && lng.HasValue)
        {
            url += $"?lat={Coord(lat.Value)}&lng={Coord(lng.Value)}";
        }
        return await http.GetFromJsonAsync<ApiEnvelope<PagedResult<ShelterSummaryDto>>>(url, ct);
    }

    public async Task<ApiEnvelope<ShelterDto>?> GetShelterAsync(Guid id, CancellationToken ct = default)
        => await http.GetFromJsonAsync<ApiEnvelope<ShelterDto>>($"api/shelters/{id}", ct);

    public async Task<HttpResponseMessage> CreateShelterAsync(CreateShelterRequest request, CancellationToken ct = default)
        => await http.PostAsJsonAsync("api/shelters", request, ct);

    public async Task<HttpResponseMessage> UpdateShelterAsync(Guid id, UpdateShelterRequest request, CancellationToken ct = default)
        => await http.PutAsJsonAsync($"api/shelters/{id}", request, ct);

    public async Task<HttpResponseMessage> UpdateOccupancyAsync(Guid id, UpdateOccupancyRequest request, CancellationToken ct = default)
        => await http.PatchAsJsonAsync($"api/shelters/{id}/occupancy", request, ct);

    public async Task<ApiEnvelope<ShelterSummaryDto>?> RecommendShelterAsync(double lat, double lng, CancellationToken ct = default)
    {
        // 404 is semantic here: "no recommendation available", not a transport failure.
        var response = await http.GetAsync($"api/shelters/recommend?lat={Coord(lat)}&lng={Coord(lng)}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ApiEnvelope<ShelterSummaryDto>>(cancellationToken: ct);
    }

    /// <summary>Ranked by suitability (distance + free capacity + facilities) with the reasons why.</summary>
    public async Task<IReadOnlyList<ShelterRecommendationDto>> GetRecommendationsAsync(
        double lat,
        double lng,
        int count = 3,
        CancellationToken ct = default)
    {
        try
        {
            var envelope = await http.GetFromJsonAsync<ApiEnvelope<List<ShelterRecommendationDto>>>(
                $"api/shelters/recommendations?lat={Coord(lat)}&lng={Coord(lng)}&count={count}", ct);
            return envelope?.Data ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException)
        {
            return [];
        }
    }

    // The WASM client runs under the browser's culture, where "23,81" would be a valid double literal.
    private static string Coord(double value) => value.ToString(CultureInfo.InvariantCulture);
}

public sealed record ShelterRecommendationDto(
    Guid Id,
    string Name,
    GeoPoint Location,
    int Capacity,
    int Occupancy,
    IReadOnlyList<string> Facilities,
    double DistanceKm,
    int FreeSpaces,
    int OccupancyPercent,
    IReadOnlyList<string> Reasons);
