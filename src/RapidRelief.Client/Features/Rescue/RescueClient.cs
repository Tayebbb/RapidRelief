using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Client.Features.Rescue;

public sealed record QueueItemDto(
    Guid IncidentId,
    DisasterType Type,
    Severity Severity,
    IncidentStatus Status,
    GeoPoint Location,
    string Summary,
    bool IsSos,
    double? PriorityScore,
    DateTimeOffset ReportedAtUtc,
    string Band,
    double? DistanceKm);

public sealed record MissionLogDto(string StatusUpdate, string Message, DateTimeOffset TimestampUtc);

public sealed record RescueMissionDto(
    Guid Id,
    Guid IncidentId,
    Guid AssignedTeamId,
    string TeamName,
    string MissionTitle,
    string Priority,
    MissionStatus Status,
    DateTimeOffset AssignedAtUtc,
    DateTimeOffset? AcceptedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? OnSceneAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string OutcomeNotes,
    string? RejectionReason,
    IReadOnlyList<MissionLogDto> Logs);

public sealed record RescueTeamDto(
    Guid Id,
    string TeamName,
    string Specialization,
    string ContactNumber,
    string Status,
    Guid TeamLeadUserId,
    GeoPoint? CurrentLocation,
    int ActiveMissionCount);

public sealed record TeamSuitabilityDto(
    Guid TeamId,
    string TeamName,
    string Specialization,
    string Status,
    double? DistanceKm,
    int ActiveMissions,
    IReadOnlyList<string> Reasons);

public sealed record RescueDashboardDto(
    IReadOnlyDictionary<string, int> QueueByBand,
    IReadOnlyList<QueueItemDto> Critical,
    IReadOnlyList<QueueItemDto> Nearby,
    int AssignedMissions,
    int ActiveMissions,
    int CompletedMissions,
    RescueTeamDto? MyTeam);

public sealed record RescueActionResult(RescueMissionDto? Mission, string? Error)
{
    public bool Ok => Mission is not null;

    public static RescueActionResult Success(RescueMissionDto mission) => new(mission, null);

    public static RescueActionResult Failure(string error) => new(null, error);
}

public sealed class RescueClient(HttpClient http)
{
    private const string BasePath = "api/rescue";

    public async Task<RescueDashboardDto?> GetDashboardAsync(GeoPoint? origin = null, CancellationToken ct = default)
    {
        try
        {
            var url = $"{BasePath}/dashboard";
            if (origin is not null)
            {
                url += $"?lat={Coord(origin.Latitude)}&lng={Coord(origin.Longitude)}";
            }

            var envelope = await http.GetFromJsonAsync<ApiEnvelope<RescueDashboardDto>>(url, ct);
            return envelope?.Data;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<QueueItemDto>> GetQueueAsync(
        string? band = null,
        GeoPoint? origin = null,
        int pageSize = 25,
        CancellationToken ct = default)
    {
        try
        {
            var url = $"{BasePath}/queue?pageSize={pageSize}";
            if (!string.IsNullOrWhiteSpace(band))
            {
                url += $"&band={band}";
            }

            if (origin is not null)
            {
                url += $"&lat={Coord(origin.Latitude)}&lng={Coord(origin.Longitude)}";
            }

            var envelope = await http.GetFromJsonAsync<ApiEnvelope<PagedResult<QueueItemDto>>>(url, ct);
            return envelope?.Data?.Items ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException)
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<RescueMissionDto>> GetMissionsAsync(
        bool mine = true,
        bool activeOnly = false,
        Guid? incidentId = null,
        CancellationToken ct = default)
    {
        try
        {
            var url = $"{BasePath}/missions?mine={mine.ToString().ToLowerInvariant()}&activeOnly={activeOnly.ToString().ToLowerInvariant()}";
            if (incidentId is { } id)
            {
                url += $"&incidentId={id}";
            }

            var envelope = await http.GetFromJsonAsync<ApiEnvelope<PagedResult<RescueMissionDto>>>(url, ct);
            return envelope?.Data?.Items ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException)
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<RescueTeamDto>> GetTeamsAsync(CancellationToken ct = default)
    {
        try
        {
            var envelope = await http.GetFromJsonAsync<ApiEnvelope<List<RescueTeamDto>>>($"{BasePath}/teams", ct);
            return envelope?.Data ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException)
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<TeamSuitabilityDto>> GetSuitableTeamsAsync(Guid incidentId, CancellationToken ct = default)
    {
        try
        {
            var envelope = await http.GetFromJsonAsync<ApiEnvelope<List<TeamSuitabilityDto>>>(
                $"{BasePath}/teams/suitable?incidentId={incidentId}", ct);
            return envelope?.Data ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException)
        {
            return [];
        }
    }

    public Task<RescueActionResult> AcceptIncidentAsync(Guid incidentId, Guid? teamId = null, CancellationToken ct = default)
        => PostAsync($"{BasePath}/missions", new { incidentId, teamId, missionTitle = (string?)null, priority = (string?)null }, ct);

    public Task<RescueActionResult> AcknowledgeAsync(Guid missionId, CancellationToken ct = default)
        => PostAsync($"{BasePath}/missions/{missionId}/accept", new { }, ct);

    public Task<RescueActionResult> RejectAsync(Guid missionId, string reason, CancellationToken ct = default)
        => PostAsync($"{BasePath}/missions/{missionId}/reject", new { reason }, ct);

    public Task<RescueActionResult> ReassignAsync(Guid missionId, Guid teamId, string? reason = null, CancellationToken ct = default)
        => PostAsync($"{BasePath}/missions/{missionId}/reassign", new { teamId, reason }, ct);

    public Task<RescueActionResult> UpdateStatusAsync(Guid missionId, MissionStatus status, string? notes = null, CancellationToken ct = default)
        => PostAsync($"{BasePath}/missions/{missionId}/status", new { status, notes }, ct);

    public async Task<string?> SetTeamStatusAsync(string status, CancellationToken ct = default)
    {
        try
        {
            var response = await http.PostAsJsonAsync($"{BasePath}/teams/mine/status", new { status }, ct);
            if (response.IsSuccessStatusCode)
            {
                return null;
            }

            return response.StatusCode switch
            {
                HttpStatusCode.Conflict => "Close the active mission before changing your status.",
                HttpStatusCode.NotFound => "You are not on a team yet — accept a mission to create one.",
                _ => "Could not change your status. Please try again.",
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return "Could not reach the server.";
        }
    }

    public async Task ReportPositionAsync(GeoPoint position, CancellationToken ct = default)
    {
        try
        {
            await http.PostAsJsonAsync($"{BasePath}/teams/mine/position",
                new { latitude = position.Latitude, longitude = position.Longitude, status = (string?)null }, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Position reporting is best-effort; the HUD keeps working without it.
        }
    }

    private async Task<RescueActionResult> PostAsync(string url, object payload, CancellationToken ct)
    {
        try
        {
            var response = await http.PostAsJsonAsync(url, payload, ct);
            if (response.IsSuccessStatusCode)
            {
                var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<RescueMissionDto>>(cancellationToken: ct);
                return envelope?.Data is { } mission
                    ? RescueActionResult.Success(mission)
                    : RescueActionResult.Failure("The action was saved but could not be read back.");
            }

            var detail = await ReadDetailAsync(response, ct);
            return RescueActionResult.Failure(detail ?? response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => "Your session expired. Sign in again to continue.",
                HttpStatusCode.Forbidden => "This mission belongs to another team.",
                HttpStatusCode.Conflict => "That step is no longer valid — refresh the mission.",
                HttpStatusCode.NotFound => "The mission or incident no longer exists.",
                HttpStatusCode.BadRequest => "Check the details and try again.",
                HttpStatusCode.ServiceUnavailable => "Rescue data is unavailable in limited mode.",
                _ => "Something went wrong. Please try again.",
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return RescueActionResult.Failure("Could not reach the server. Check your connection and try again.");
        }
    }

    /// <summary>Server-authored ProblemDetails text explains conflicts better than a generic string.</summary>
    private static async Task<string?> ReadDetailAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemView>(cancellationToken: ct);
            return string.IsNullOrWhiteSpace(problem?.Detail) ? null : problem!.Detail;
        }
        catch (Exception ex) when (ex is HttpRequestException or NotSupportedException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private sealed record ProblemView(string? Title, string? Detail);

    private static string Coord(double value) => value.ToString(CultureInfo.InvariantCulture);
}
