using System.Net;
using System.Net.Http.Json;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Client.Features.Relief;

public sealed record CreateReliefRequest(
    ResourceType Type,
    int Quantity,
    int RecipientCount,
    string? Urgency,
    double Latitude,
    double Longitude,
    string? DeliveryAddress,
    string? Notes,
    Guid? IncidentId,
    string? IdempotencyKey);

public sealed record ReliefRequestDto(
    Guid Id,
    Guid RequesterId,
    ResourceType Type,
    int Quantity,
    int RecipientCount,
    string Urgency,
    ReliefStatus Status,
    GeoPoint Location,
    string DeliveryAddress,
    string Notes,
    Guid? IncidentId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ReliefActionResult(ReliefRequestDto? Request, string? Error)
{
    public bool Ok => Request is not null;

    public static ReliefActionResult Success(ReliefRequestDto request) => new(request, null);

    public static ReliefActionResult Failure(string error) => new(null, error);
}

public sealed class ReliefClient(HttpClient http)
{
    private const string BasePath = "api/relief/requests";

    public async Task<ReliefActionResult> CreateAsync(CreateReliefRequest request, CancellationToken ct = default)
    {
        try
        {
            var response = await http.PostAsJsonAsync(BasePath, request, ct);
            if (response.IsSuccessStatusCode)
            {
                var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<ReliefRequestDto>>(cancellationToken: ct);
                return envelope?.Data is { } created
                    ? ReliefActionResult.Success(created)
                    : ReliefActionResult.Failure("The request was accepted but could not be read back.");
            }

            return ReliefActionResult.Failure(response.StatusCode switch
            {
                HttpStatusCode.BadRequest => "Please check the amounts you entered and try again.",
                HttpStatusCode.Unauthorized => "Your session expired. Sign in again to continue.",
                HttpStatusCode.TooManyRequests => "Too many requests. Wait a moment and try again.",
                HttpStatusCode.ServiceUnavailable => "Relief service is running in limited mode. Call 999 for urgent needs.",
                _ => "Something went wrong while sending your request.",
            });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ReliefActionResult.Failure("Could not reach the server. Check your connection and try again.");
        }
    }

    public async Task<IReadOnlyList<ReliefRequestDto>> GetMineAsync(CancellationToken ct = default)
    {
        try
        {
            var envelope = await http.GetFromJsonAsync<ApiEnvelope<PagedResult<ReliefRequestDto>>>($"{BasePath}/mine", ct);
            return envelope?.Data?.Items ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException)
        {
            return [];
        }
    }

    /// <summary>Government triage queue — every request, newest and most urgent first.</summary>
    public async Task<IReadOnlyList<ReliefRequestDto>> GetQueueAsync(ReliefStatus? status = null, CancellationToken ct = default)
    {
        var url = $"{BasePath}?page=1&pageSize=200";
        if (status is { } wanted)
        {
            url += $"&status={wanted}";
        }

        try
        {
            var envelope = await http.GetFromJsonAsync<ApiEnvelope<PagedResult<ReliefRequestDto>>>(url, ct);
            return envelope?.Data?.Items ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or NotSupportedException)
        {
            return [];
        }
    }

    public async Task<ReliefActionResult> CancelAsync(Guid id, CancellationToken ct = default)
    {        try
        {
            var response = await http.PostAsync($"{BasePath}/{id}/cancel", content: null, ct);
            if (response.IsSuccessStatusCode)
            {
                var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<ReliefRequestDto>>(cancellationToken: ct);
                return envelope?.Data is { } cancelled
                    ? ReliefActionResult.Success(cancelled)
                    : ReliefActionResult.Failure("Cancelled, but the request could not be read back.");
            }

            return ReliefActionResult.Failure(response.StatusCode == HttpStatusCode.Conflict
                ? "Supplies are already on their way — this request can no longer be cancelled."
                : "Could not cancel the request. Please try again.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ReliefActionResult.Failure("Could not reach the server. Check your connection and try again.");
        }
    }

    /// <summary>Citizen-facing stage labels for the frozen ReliefStatus values.</summary>
    public static string StageLabel(ReliefStatus status) => status switch
    {
        ReliefStatus.Pending => "Requested",
        ReliefStatus.Approved => "Accepted",
        ReliefStatus.Allocated => "Preparing",
        ReliefStatus.Dispatched => "Dispatched",
        ReliefStatus.Delivered => "Delivered",
        ReliefStatus.Rejected => "Closed",
        _ => status.ToString(),
    };

    public static int StageIndex(ReliefStatus status) => status switch
    {
        ReliefStatus.Pending => 1,
        ReliefStatus.Approved => 2,
        ReliefStatus.Allocated => 3,
        ReliefStatus.Dispatched => 4,
        ReliefStatus.Delivered => 5,
        _ => 0,
    };
}
