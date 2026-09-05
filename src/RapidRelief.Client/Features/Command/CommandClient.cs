using System.Globalization;
using System.Net.Http.Json;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Client.Features.Command;

/// <summary>Hand-mirrored wire records (D-045) for the government command surfaces.</summary>
public sealed record OpsKpiDto(
    int ActiveIncidents,
    int CriticalIncidents,
    int SosOpen,
    int Unassigned,
    int AwaitingTeam,
    int InProgress,
    int ResolvedLast24h,
    int NewLast24h,
    double? AvgResponseMinutes,
    double? AvgResolutionMinutes,
    double ResolutionRatePercent,
    int TotalIncidents);

public sealed record NamedCountDto(string Key, int Count);

public sealed record TimeBucketDto(string Day, int Reported, int Resolved);

public sealed record HotspotDto(
    string Area,
    GeoPoint Location,
    int Total,
    int Critical,
    int Last6h,
    int Previous6h,
    string Trend);

public sealed record IncidentOpsSummaryDto(
    OpsKpiDto Kpi,
    IReadOnlyList<NamedCountDto> ByStatus,
    IReadOnlyList<NamedCountDto> ByType,
    IReadOnlyList<NamedCountDto> BySeverity,
    IReadOnlyList<TimeBucketDto> Daily,
    IReadOnlyList<HotspotDto> Hotspots,
    DateTimeOffset GeneratedAtUtc);

public sealed record AuditFacetsDto(IReadOnlyList<string> Actions, IReadOnlyList<string> EntityTypes);

public sealed record ReliefResourceRequest(
    string Name,
    ResourceType Category,
    double TotalQuantity,
    double AllocatedQuantity,
    string? Unit,
    string? WarehouseLocation);

public sealed record ReliefResourceDto(
    Guid Id,
    string Name,
    ResourceType Category,
    double TotalQuantity,
    double AllocatedQuantity,
    double AvailableQuantity,
    string Unit,
    string WarehouseLocation,
    double OpenDemand,
    DateTimeOffset UpdatedAtUtc);

public sealed record ReliefResourceGapDto(ResourceType Category, double OpenDemand);

public sealed record ReliefInventoryDto(
    IReadOnlyList<ReliefResourceDto> Items,
    IReadOnlyList<ReliefResourceGapDto> UncoveredDemand);

public sealed record UpdateTeamRequest(string TeamName, string? Specialization, string? ContactNumber, string? Status);

/// <summary>Search state for the incident board — one object so the page can bind it directly.</summary>
public sealed class IncidentFilter
{
    public string? Query { get; set; }
    public IncidentStatus? Status { get; set; }
    public DisasterType? Type { get; set; }
    public Severity? Severity { get; set; }
    public bool SosOnly { get; set; }
    public bool UnassignedOnly { get; set; }
    public int PageSize { get; set; } = 100;

    public bool IsEmpty => string.IsNullOrWhiteSpace(Query) && Status is null && Type is null
        && Severity is null && !SosOnly && !UnassignedOnly;
}

/// <summary>
/// Read/write surface for the EOC. Every call fails soft: pages render an empty or error state,
/// never an exception, because the command centre must stay usable in a degraded network.
/// </summary>
public sealed class CommandClient(HttpClient http)
{
    public async Task<IncidentOpsSummaryDto?> GetOpsSummaryAsync(int days = 14, CancellationToken ct = default)
        => await GetAsync<IncidentOpsSummaryDto>($"api/incidents/ops/summary?days={days}", ct);

    public async Task<IReadOnlyList<IncidentSearchRow>> SearchIncidentsAsync(IncidentFilter filter, CancellationToken ct = default)
    {
        var url = $"api/incidents?page=1&pageSize={filter.PageSize}";
        if (!string.IsNullOrWhiteSpace(filter.Query))
        {
            url += $"&q={Uri.EscapeDataString(filter.Query.Trim())}";
        }

        if (filter.Status is { } status)
        {
            url += $"&status={status}";
        }

        if (filter.Type is { } type)
        {
            url += $"&type={type}";
        }

        if (filter.Severity is { } severity)
        {
            url += $"&severity={severity}";
        }

        if (filter.SosOnly)
        {
            url += "&sos=true";
        }

        if (filter.UnassignedOnly)
        {
            url += "&unassigned=true";
        }

        var paged = await GetAsync<PagedResult<IncidentSearchRow>>(url, ct);
        return paged?.Items ?? [];
    }

    public async Task<string?> ResolveIncidentAsync(Guid id, string notes, CancellationToken ct = default)
        => await PostAsync($"api/incidents/{id}/resolve", new { notes }, ct);

    public async Task<PagedResult<AuditEntryDto>?> GetAuditAsync(
        string? action = null,
        string? entityType = null,
        string? q = null,
        int hours = 0,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        var url = $"api/audit?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(action))
        {
            url += $"&action={Uri.EscapeDataString(action)}";
        }

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            url += $"&entityType={Uri.EscapeDataString(entityType)}";
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            url += $"&q={Uri.EscapeDataString(q.Trim())}";
        }

        if (hours > 0)
        {
            url += $"&hours={hours}";
        }

        return await GetAsync<PagedResult<AuditEntryDto>>(url, ct);
    }

    public async Task<AuditFacetsDto?> GetAuditFacetsAsync(CancellationToken ct = default)
        => await GetAsync<AuditFacetsDto>("api/audit/actions", ct);

    public async Task<ReliefInventoryDto?> GetInventoryAsync(CancellationToken ct = default)
        => await GetAsync<ReliefInventoryDto>("api/relief/resources", ct);

    public async Task<string?> SaveResourceAsync(Guid? id, ReliefResourceRequest request, CancellationToken ct = default)
        => id is { } existing
            ? await PutAsync($"api/relief/resources/{existing}", request, ct)
            : await PostAsync("api/relief/resources", request, ct);

    public async Task<PagedResult<UserSummaryDto>?> GetUsersAsync(int page = 1, int pageSize = 100, CancellationToken ct = default)
        => await GetAsync<PagedResult<UserSummaryDto>>($"api/auth/users?page={page}&pageSize={pageSize}", ct);

    public async Task<string?> SetUserLockAsync(Guid id, bool locked, CancellationToken ct = default)
        => await PostAsync($"api/auth/users/{id}/lock", new { locked }, ct);

    public async Task<string?> SetUserRolesAsync(Guid id, IReadOnlyList<string> roles, CancellationToken ct = default)
        => await PutAsync($"api/auth/users/{id}/roles", new { roles }, ct);

    public async Task<string?> CreateTeamAsync(string teamName, string? specialization, string? contactNumber, CancellationToken ct = default)
        => await PostAsync("api/rescue/teams", new { teamName, specialization, contactNumber }, ct);

    public async Task<string?> UpdateTeamAsync(Guid id, UpdateTeamRequest request, CancellationToken ct = default)
        => await PutAsync($"api/rescue/teams/{id}", request, ct);

    public async Task<string?> UpdateReliefStatusAsync(Guid id, ReliefStatus status, string? note, CancellationToken ct = default)
        => await PostAsync($"api/relief/requests/{id}/status", new { status, note }, ct);

    public static string FormatMinutes(double? minutes) => minutes switch
    {
        null => "—",
        < 1 => "<1 min",
        < 60 => $"{minutes.Value:F0} min",
        < 1440 => $"{minutes.Value / 60:F1} h",
        _ => $"{minutes.Value / 1440:F1} d",
    };

    public static string FormatCoordinate(double value) => value.ToString("F4", CultureInfo.InvariantCulture);

    private async Task<T?> GetAsync<T>(string url, CancellationToken ct)
    {
        try
        {
            var envelope = await http.GetFromJsonAsync<ApiEnvelope<T>>(url, ct);
            return envelope is null ? default : envelope.Data;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException)
        {
            return default;
        }
    }

    private async Task<string?> PostAsync(string url, object body, CancellationToken ct)
    {
        try
        {
            var response = await http.PostAsJsonAsync(url, body, ct);
            return response.IsSuccessStatusCode ? null : await ProblemDetailReader.ReadAsync(response, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return "Could not reach the server.";
        }
    }

    private async Task<string?> PutAsync(string url, object body, CancellationToken ct)
    {
        try
        {
            var response = await http.PutAsJsonAsync(url, body, ct);
            return response.IsSuccessStatusCode ? null : await ProblemDetailReader.ReadAsync(response, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return "Could not reach the server.";
        }
    }
}

/// <summary>Trimmed incident projection for the board — the full DTO is fetched only on drill-down.</summary>
public sealed record IncidentSearchRow(
    Guid Id,
    Guid ReporterId,
    string Title,
    string Description,
    DisasterType DisasterType,
    Severity Severity,
    IncidentStatus Status,
    GeoPoint Location,
    string AddressOrArea,
    int AffectedPeopleCount,
    bool IsSos,
    string? ContactPhone,
    double? PriorityScore,
    string AiSummary,
    Guid? PossibleDuplicateOfId,
    Guid? AssignedTeamId,
    Guid? AssignedMissionId,
    string? MissionStage,
    string? RejectionReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ResolvedAtUtc);

internal static class ProblemDetailReader
{
    public static async Task<string> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemShape>(cancellationToken: ct);
            if (!string.IsNullOrWhiteSpace(problem?.Detail))
            {
                return problem!.Detail!;
            }

            if (!string.IsNullOrWhiteSpace(problem?.Title))
            {
                return problem!.Title!;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException or System.Text.Json.JsonException)
        {
            // Fall through to the status-based message.
        }

        return response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => "Your session expired. Sign in again.",
            System.Net.HttpStatusCode.Forbidden => "You do not have permission to do that.",
            System.Net.HttpStatusCode.NotFound => "That record no longer exists.",
            System.Net.HttpStatusCode.Conflict => "That action conflicts with the current state.",
            _ => "The action could not be completed.",
        };
    }

    private sealed record ProblemShape(string? Title, string? Detail);
}
