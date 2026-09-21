using System.Net.Http.Json;
using RapidRelief.Shared.Contracts.Common;

namespace RapidRelief.Client.Features.Alerts;

public sealed class AlertsApi(HttpClient http) : IAlertsApi
{
    private const string BasePath = "api/alerts";

    public async Task<ApiEnvelope<PagedResult<AlertDto>>?> GetAsync(int page = 1, int pageSize = 20, CancellationToken ct = default)
        => await http.GetFromJsonAsync<ApiEnvelope<PagedResult<AlertDto>>>($"{BasePath}?page={page}&pageSize={pageSize}", ct);

    public async Task<IReadOnlyList<AlertDto>> GetActiveAsync(CancellationToken ct = default)
    {
        var result = await http.GetFromJsonAsync<ApiEnvelope<List<AlertDto>>>($"{BasePath}/active", ct);
        return result?.Data ?? [];
    }

    public Task<HttpResponseMessage> CreateAsync(CreateAlertRequest request, CancellationToken ct = default)
        => http.PostAsJsonAsync(BasePath, request, ct);

    public Task<HttpResponseMessage> RevokeAsync(Guid id, CancellationToken ct = default)
        => http.PostAsync($"{BasePath}/{id:D}/revoke", content: null, ct);
}
