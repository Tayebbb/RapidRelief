using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RapidRelief.Shared.Contracts.Common;

namespace RapidRelief.Client.Common.Realtime;

/// <summary>Why a page fetch produced nothing — only a 401 means "stop asking".</summary>
public enum NotificationFetchOutcome
{
    Ok = 0,
    Failed = 1,
    Unauthorized = 2,
}

/// <summary>Outcome of one inbox fetch; <see cref="Page"/> is set only when <c>Outcome</c> is Ok.</summary>
public sealed record NotificationFetch(NotificationFetchOutcome Outcome, NotificationPage? Page)
{
    public static NotificationFetch Failed { get; } = new(NotificationFetchOutcome.Failed, null);

    public static NotificationFetch Unauthorized { get; } = new(NotificationFetchOutcome.Unauthorized, null);
}

/// <summary>
/// Inbox transport. Every failure — offline, timeout, 401, 503 degraded (D-005), garbage body —
/// surfaces as "no result" so the notification UI can never raise an error banner of its own.
/// </summary>
public interface INotificationsApi
{
    Task<NotificationFetch> GetAsync(string? since, int? limit, CancellationToken ct = default);

    Task<bool> MarkReadAsync(Guid id, CancellationToken ct = default);

    Task<int?> MarkAllReadAsync(CancellationToken ct = default);

    Task<int?> GetUnreadCountAsync(CancellationToken ct = default);
}

/// <inheritdoc cref="INotificationsApi"/>
public sealed class NotificationsApi : INotificationsApi
{
    internal const string BasePath = "api/realtime/notifications";
    private static readonly TimeSpan RequestBudget = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http;

    /// <param name="http">
    /// D-044: this singleton's OWN client, built from the same DevRoleHandler →
    /// AuthMessageHandler chain as the main scoped client, so Bearer and X-Dev-Role behave
    /// exactly as they do for every other API call.
    /// </param>
    public NotificationsApi(HttpClient http) => _http = http;

    public async Task<NotificationFetch> GetAsync(string? since, int? limit, CancellationToken ct = default)
    {
        var query = new List<string>(2);
        if (!string.IsNullOrEmpty(since))
        {
            query.Add($"since={Uri.EscapeDataString(since)}");
        }

        if (limit is not null)
        {
            query.Add($"limit={limit.Value}");
        }

        var url = query.Count > 0 ? $"{BasePath}?{string.Join('&', query)}" : BasePath;
        try
        {
            using var cts = Budget(ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await _http.SendAsync(request, cts.Token);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return NotificationFetch.Unauthorized;
            }

            if (!response.IsSuccessStatusCode)
            {
                return NotificationFetch.Failed;
            }

            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<NotificationPage>>(cts.Token);
            return envelope?.Data is { } page
                ? new NotificationFetch(NotificationFetchOutcome.Ok, page)
                : NotificationFetch.Failed;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return NotificationFetch.Failed;
        }
    }

    public async Task<bool> MarkReadAsync(Guid id, CancellationToken ct = default)
    {
        try
        {
            using var cts = Budget(ct);
            using var response = await _http.PatchAsync($"{BasePath}/{id:D}/read", content: null, cts.Token);
            return response.StatusCode is HttpStatusCode.NoContent or HttpStatusCode.OK;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            return false;
        }
    }

    public async Task<int?> MarkAllReadAsync(CancellationToken ct = default)
    {
        var envelope = await SendAsync<ApiEnvelope<MarkedResponse>>(HttpMethod.Post, $"{BasePath}/read-all", ct);
        return envelope?.Data.Marked;
    }

    public async Task<int?> GetUnreadCountAsync(CancellationToken ct = default)
    {
        var envelope = await SendAsync<ApiEnvelope<UnreadCountResponse>>(
            HttpMethod.Get, $"{BasePath}/unread-count", ct);
        return envelope?.Data.Count;
    }

    private async Task<T?> SendAsync<T>(HttpMethod method, string url, CancellationToken ct)
        where T : class
    {
        try
        {
            using var cts = Budget(ct);
            using var request = new HttpRequestMessage(method, url);
            using var response = await _http.SendAsync(request, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync<T>(cts.Token);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return null;
        }
    }

    private static CancellationTokenSource Budget(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(RequestBudget);
        return cts;
    }
}
