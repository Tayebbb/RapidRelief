using System.Net.Http.Json;

using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Client.Features.CommandCenter;

public class CommandCenterClient(HttpClient http)
{
    public async Task<ApiEnvelope<CommandCenterOverviewDto>?> GetOverviewAsync(CancellationToken ct = default)
    {
        var response = await http.GetAsync("/api/command-center/overview", ct);
        
        // Throw an exception on non-success so that the UI can catch it (especially 503 for degraded mode)
        response.EnsureSuccessStatusCode();
        
        return await response.Content.ReadFromJsonAsync<ApiEnvelope<CommandCenterOverviewDto>>(cancellationToken: ct);
    }
}
