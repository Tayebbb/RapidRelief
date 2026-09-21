using System.Text;
using System.Text.Json;

namespace RapidRelief.Api.Features.Ai.OpenRouter;

/// <summary>
/// Real OpenRouter transport (D-060): named HttpClient "openrouter" (BaseAddress pinned,
/// Timeout = Infinite), POST api/v1/chat/completions with the API key as a per-request
/// Authorization: Bearer header (never in the URL, never logged) plus X-Title attribution.
/// D-026 timeouts via per-request linked CTS; zero retries — a 429 is just a breaker-counted
/// failure. D-063 three-way classification: non-2xx except 403 → AiProviderUnavailableException
/// (body never read); 403 → AiProviderBlockedException (status alone, body never read); 2xx with
/// a top-level error and no choices → Unavailable reading ONLY error.code + sanitized
/// error.metadata.error_type (error.message is never read); choices[0].finish_reason == "error"
/// → Unavailable ("provider mid-generation error").
/// </summary>
internal sealed class OpenRouterClient : IOpenRouterClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;

    public OpenRouterClient(IHttpClientFactory httpClientFactory, IConfiguration config)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
    }

    public async Task<string> SendAsync(string requestBody, bool isVision, CancellationToken ct = default)
    {
        var maxAttempts = Math.Clamp(_config.GetValue("Ai:OpenRouter:MaxAttempts", 2), 1, 4);
        var baseDelayMs = Math.Clamp(_config.GetValue("Ai:OpenRouter:RetryBaseDelayMs", 250), 0, 5_000);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await SendOnceAsync(requestBody, isVision, ct);
            }
            catch (AiProviderUnavailableException ex) when (attempt < maxAttempts && ex.IsTransient)
            {
                // Exponential backoff with jitter: a provider that just rate-limited us must not
                // be hit again on the same millisecond by every queued incident.
                var delay = baseDelayMs * (1 << (attempt - 1));
                var jitter = Random.Shared.Next(0, Math.Max(1, delay / 2));
                await Task.Delay(TimeSpan.FromMilliseconds(delay + jitter), ct);
            }
        }
    }

    private async Task<string> SendOnceAsync(string requestBody, bool isVision, CancellationToken ct)
    {
        var apiKey = _config["Ai:OpenRouter:ApiKey"] ?? string.Empty;
        var timeoutSeconds = isVision
            ? _config.GetValue("Ai:OpenRouter:TimeoutSecondsVision", 20)
            : _config.GetValue("Ai:OpenRouter:TimeoutSecondsText", 10);

        var client = _httpClientFactory.CreateClient("openrouter");
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "api/v1/chat/completions")
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
        };
        httpRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
        httpRequest.Headers.TryAddWithoutValidation("X-Title", "RapidRelief");

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(httpRequest, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // caller cancellation is not a provider failure
        }
        catch (OperationCanceledException)
        {
            throw new AiProviderUnavailableException(
                $"OpenRouter {(isVision ? "vision" : "text")} request timed out after {timeoutSeconds} s",
                isTransient: true);
        }
        catch (HttpRequestException ex)
        {
            // Metadata-only message; the original (host-level detail, no headers) rides as inner.
            throw new AiProviderUnavailableException($"OpenRouter request failed: {ex.GetType().Name}", ex, isTransient: true);
        }

        using (response)
        {
            if ((int)response.StatusCode == 403)
            {
                // D-064: OpenRouter signals input moderation with the status alone — body never read.
                throw new AiProviderBlockedException("OpenRouter flagged the input (HTTP 403)");
            }

            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                // Only overload and server-side faults are worth a second attempt; a 400 or a 402
                // will fail identically and just delays the fallback the caller already has.
                var transient = status == 429 || status >= 500;
                throw new AiProviderUnavailableException($"OpenRouter returned HTTP {status}", transient);
            }

            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw new AiProviderUnavailableException(
                    $"OpenRouter response read timed out after {timeoutSeconds} s", isTransient: true);
            }

            ThrowOnEmbeddedProviderFailure(body);
            return body;
        }
    }

    /// <summary>
    /// D-063 checks 2+3 — bounded body read: only error.code, error.metadata.error_type and
    /// choices[0].finish_reason are ever inspected; error.message is NEVER read into a string.
    /// An unparseable 2xx body returns verbatim so the parsers reject it (counts — unchanged posture).
    /// </summary>
    private static void ThrowOnEmbeddedProviderFailure(string body)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var hasChoices = root.TryGetProperty("choices", out var choices)
                && choices.ValueKind == JsonValueKind.Array
                && choices.GetArrayLength() > 0;

            if (root.TryGetProperty("error", out var error) && !hasChoices)
            {
                throw new AiProviderUnavailableException(
                    $"OpenRouter 200-level error: code {ErrorCode(error)}, type {ErrorType(error)}");
            }

            if (hasChoices
                && choices[0].ValueKind == JsonValueKind.Object
                && choices[0].TryGetProperty("finish_reason", out var finish)
                && finish.ValueKind == JsonValueKind.String
                && finish.GetString() == "error")
            {
                throw new AiProviderUnavailableException("OpenRouter provider mid-generation error");
            }
        }
    }

    private static string ErrorCode(JsonElement error)
        => error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var code)
            ? code.ValueKind switch
            {
                JsonValueKind.Number => SanitizeForMessage(code.GetRawText()),
                JsonValueKind.String => SanitizeForMessage(code.GetString()!),
                _ => "unknown",
            }
            : "unknown";

    private static string ErrorType(JsonElement error)
        => error.ValueKind == JsonValueKind.Object
           && error.TryGetProperty("metadata", out var metadata)
           && metadata.ValueKind == JsonValueKind.Object
           && metadata.TryGetProperty("error_type", out var type)
           && type.ValueKind == JsonValueKind.String
            ? SanitizeForMessage(type.GetString()!)
            : "unknown";

    /// <summary>Strips to [A-Za-z0-9_] and clamps to 32 chars — safe to embed in exception messages.</summary>
    private static string SanitizeForMessage(string raw)
    {
        var cleaned = new string(raw.Where(c => char.IsAsciiLetterOrDigit(c) || c == '_').ToArray());
        return cleaned.Length <= 32 ? cleaned : cleaned[..32];
    }
}
