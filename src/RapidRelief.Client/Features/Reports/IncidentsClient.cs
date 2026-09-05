using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Client.Features.Reports;

/// <summary>Hand-mirrored wire records (D-019/D-045) pinned server-side by IncidentWireContractTests.</summary>
public sealed record CreateIncidentRequest(
    string Title,
    string Description,
    DisasterType DisasterType,
    Severity Severity,
    double Latitude,
    double Longitude,
    string? AddressOrArea,
    int AffectedPeopleCount,
    bool IsSos,
    string? ContactPhone,
    IReadOnlyList<string>? PhotoPaths,
    string? IdempotencyKey);

public sealed record IncidentMediaDto(Guid Id, string Url, string MediaType, long SizeBytes, DateTimeOffset UploadedAtUtc);

public sealed record IncidentStatusEntryDto(IncidentStatus FromStatus, IncidentStatus ToStatus, string Notes, DateTimeOffset ChangedAtUtc);

public sealed record IncidentDto(
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
    DateTimeOffset? ResolvedAtUtc,
    IReadOnlyList<IncidentMediaDto> Media,
    IReadOnlyList<IncidentStatusEntryDto> Timeline);

public sealed record UploadedMediaDto(string Path, string Url, long SizeBytes, string ContentType);

/// <summary>Outcome of a submit attempt — the page renders one of these three shapes, never an exception.</summary>
public sealed record IncidentSubmitResult(IncidentDto? Incident, string? Error, IReadOnlyDictionary<string, string[]>? FieldErrors)
{
    public bool Ok => Incident is not null;

    public static IncidentSubmitResult Success(IncidentDto incident) => new(incident, null, null);

    public static IncidentSubmitResult Failure(string error, IReadOnlyDictionary<string, string[]>? fieldErrors = null)
        => new(null, error, fieldErrors);
}

public sealed class IncidentsClient(HttpClient http)
{
    private const string BasePath = "api/incidents";

    public async Task<IncidentSubmitResult> CreateAsync(CreateIncidentRequest request, CancellationToken ct = default)
    {
        try
        {
            var response = await http.PostAsJsonAsync(BasePath, request, ct);
            if (response.IsSuccessStatusCode)
            {
                var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<IncidentDto>>(cancellationToken: ct);
                return envelope?.Data is { } incident
                    ? IncidentSubmitResult.Success(incident)
                    : IncidentSubmitResult.Failure("The report was accepted but could not be read back.");
            }

            return await ToFailureAsync(response, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return IncidentSubmitResult.Failure("Could not reach the server. Check your connection and try again.");
        }
    }

    public async Task<UploadedMediaDto?> UploadPhotoAsync(Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        try
        {
            using var form = new MultipartFormDataContent();
            var file = new StreamContent(content);
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(file, "file", fileName);

            var response = await http.PostAsync($"{BasePath}/media", form, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<UploadedMediaDto>>(cancellationToken: ct);
            return envelope?.Data;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    public Task<PagedResult<IncidentDto>?> GetMineAsync(int page = 1, int pageSize = 50, CancellationToken ct = default)
        => GetPagedAsync($"{BasePath}/mine?page={page}&pageSize={pageSize}", ct);

    public Task<PagedResult<IncidentDto>?> GetFeedAsync(
        IncidentStatus? status = null,
        bool unassignedOnly = false,
        int page = 1,
        int pageSize = 50,
        CancellationToken ct = default)
    {
        var url = $"{BasePath}?page={page}&pageSize={pageSize}";
        if (status is { } wanted)
        {
            url += $"&status={wanted}";
        }

        if (unassignedOnly)
        {
            url += "&unassigned=true";
        }

        return GetPagedAsync(url, ct);
    }

    public async Task<IncidentDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            var envelope = await http.GetFromJsonAsync<ApiEnvelope<IncidentDto>>($"{BasePath}/{id}", ct);
            return envelope?.Data;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException)
        {
            return null;
        }
    }

    public async Task<IncidentSubmitResult> VerifyAsync(Guid id, bool approved, string? reason, CancellationToken ct = default)
    {
        try
        {
            var response = await http.PostAsJsonAsync($"{BasePath}/{id}/verify", new { approved, reason }, ct);
            if (response.IsSuccessStatusCode)
            {
                var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<IncidentDto>>(cancellationToken: ct);
                return envelope?.Data is { } incident
                    ? IncidentSubmitResult.Success(incident)
                    : IncidentSubmitResult.Failure("The decision was saved but could not be read back.");
            }

            return await ToFailureAsync(response, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return IncidentSubmitResult.Failure("Could not reach the server. Check your connection and try again.");
        }
    }

    private async Task<PagedResult<IncidentDto>?> GetPagedAsync(string url, CancellationToken ct)
    {
        try
        {
            var envelope = await http.GetFromJsonAsync<ApiEnvelope<PagedResult<IncidentDto>>>(url, ct);
            return envelope?.Data;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException)
        {
            return null;
        }
    }

    private static async Task<IncidentSubmitResult> ToFailureAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problem = await ReadValidationProblemAsync(response, ct);
            if (problem is { Count: > 0 })
            {
                return IncidentSubmitResult.Failure("Please correct the highlighted fields.", problem);
            }
        }

        var message = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "Your session expired. Sign in again to continue.",
            HttpStatusCode.Forbidden => "Your account is not allowed to do that.",
            HttpStatusCode.Conflict => "This report has already moved on — refresh to see the latest status.",
            HttpStatusCode.TooManyRequests => "Too many submissions. Wait a moment and try again.",
            HttpStatusCode.ServiceUnavailable => "The service is running in limited mode. Call 999 for immediate help.",
            _ => "Something went wrong while saving. Please try again.",
        };
        return IncidentSubmitResult.Failure(message);
    }

    private static async Task<IReadOnlyDictionary<string, string[]>?> ReadValidationProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblem>(cancellationToken: ct);
            return problem?.Errors;
        }
        catch (Exception ex) when (ex is HttpRequestException or NotSupportedException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private sealed record ValidationProblem(Dictionary<string, string[]>? Errors);

    public static string FormatCoordinate(double value) => value.ToString("F5", CultureInfo.InvariantCulture);
}
