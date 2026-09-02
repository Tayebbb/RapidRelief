using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RapidRelief.Shared.Contracts.Common;

namespace RapidRelief.Client.Features.Assistant;

/// <summary>
/// One send attempt. <see cref="IsFallback"/> means the answer is the local canned line rather
/// than anything the server said; <see cref="Notice"/> is an optional one-line UX explanation.
/// </summary>
public sealed record AssistantSendResult(
    AssistantAnswerDto Answer,
    Guid? SessionId,
    bool Degraded,
    bool Persisted,
    bool IsFallback,
    string? Notice);

/// <summary>
/// Assistant transport. Every failure — offline, timeout, 401, 429, 400, 503, garbage body —
/// resolves to a local fallback line so the chat never dead-ends and never throws.
/// </summary>
public interface IAssistantApi
{
    Task<AssistantSendResult> SendAsync(
        Guid? sessionId, string message, double? latitude, double? longitude, CancellationToken ct = default);

    Task<AssistantHistoryResponse?> GetHistoryAsync(Guid sessionId, CancellationToken ct = default);

    Task<bool> DeleteSessionAsync(Guid sessionId, CancellationToken ct = default);
}

/// <inheritdoc cref="IAssistantApi"/>
public sealed class AssistantApi : IAssistantApi
{
    /// <summary>Pinned server-side by AssistantWireContractTests — the client build cannot see a rename.</summary>
    public const string BasePath = "api/ai/assistant";

    /// <summary>
    /// The one line the taxonomy is NOT duplicated for: the server owns canned guidance, the
    /// client only guarantees the chat never dead-ends.
    /// </summary>
    public const string FallbackText =
        "I can't reach the assistant right now. If anyone's life is at risk, call 999 now and move to safety.";

    /// <summary>Distinct from the server's "OpenRouter"/"Canned" so the page can flag offline guidance.</summary>
    public const string FallbackProvider = "Fallback";

    private const string ExpiredNotice = "Your session has expired. Sign in again to keep chatting.";
    private const string RateLimitedNotice = "You've sent a lot of messages. Wait a minute before sending another.";
    private const string DegradedNotice = "The assistant is busy right now. Try again in a moment.";
    private const string RejectedNotice = "That message couldn't be sent. Try a shorter one, or start a new chat.";

    // Longer than the server's 10 s OpenRouter budget so a slow-but-successful answer still lands.
    private static readonly TimeSpan RequestBudget = TimeSpan.FromSeconds(15);

    private readonly HttpClient _http;

    /// <param name="http">
    /// The MAIN scoped client, so the DevRoleHandler → AuthMessageHandler chain applies exactly
    /// as it does for every other API call.
    /// </param>
    public AssistantApi(HttpClient http) => _http = http;

    public async Task<AssistantSendResult> SendAsync(
        Guid? sessionId, string message, double? latitude, double? longitude, CancellationToken ct = default)
    {
        try
        {
            using var cts = Budget(ct);
            using var response = await _http.PostAsJsonAsync(
                $"{BasePath}/messages",
                new AssistantMessageRequest(sessionId, message, latitude, longitude),
                cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                return Fallback(await NoticeForAsync(response, cts.Token));
            }

            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<AssistantMessageResponse>>(cts.Token);
            return envelope?.Data is { Answer: not null } data
                ? new AssistantSendResult(data.Answer, data.SessionId, data.Degraded, data.Persisted,
                    IsFallback: false, Notice: null)
                : Fallback(null);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return Fallback(null);
        }
    }

    public async Task<AssistantHistoryResponse?> GetHistoryAsync(Guid sessionId, CancellationToken ct = default)
    {
        try
        {
            using var cts = Budget(ct);
            using var response = await _http.GetAsync($"{BasePath}/sessions/{sessionId:D}/messages", cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var envelope = await response.Content.ReadFromJsonAsync<ApiEnvelope<AssistantHistoryResponse>>(cts.Token);
            return envelope?.Data;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return null;
        }
    }

    public async Task<bool> DeleteSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        try
        {
            using var cts = Budget(ct);
            using var response = await _http.DeleteAsync($"{BasePath}/sessions/{sessionId:D}", cts.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            return false;
        }
    }

    private static AssistantSendResult Fallback(string? notice) => new(
        new AssistantAnswerDto(FallbackText, FallbackProvider, Truncated: false, DateTimeOffset.UtcNow),
        SessionId: null, Degraded: false, Persisted: false, IsFallback: true, notice);

    /// <summary>Only statuses a user can act on get a notice; the rest speak through the fallback line.</summary>
    private static async Task<string?> NoticeForAsync(HttpResponseMessage response, CancellationToken ct)
        => response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ExpiredNotice,
            HttpStatusCode.TooManyRequests => RateLimitedNotice,
            HttpStatusCode.ServiceUnavailable => DegradedNotice,
            HttpStatusCode.BadRequest => await ProblemDetailAsync(response, ct) ?? RejectedNotice,
            _ => null,
        };

    /// <summary>ProblemDetails.detail is server-authored text (e.g. "conversation full") — safe to show.</summary>
    private static async Task<string?> ProblemDetailAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("detail", out var detail) &&
                   detail.ValueKind == JsonValueKind.String &&
                   !string.IsNullOrWhiteSpace(detail.GetString())
                ? detail.GetString()
                : null;
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or OperationCanceledException)
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
