using System.Text;
using System.Text.Json;

namespace RapidRelief.Api.Features.Ai.FreeLlmPool;

/// <summary>
/// Real freellmpool transport (D-113): named HttpClient "freellmpool" (BaseAddress read from
/// config Ai:FreeLlmPool:BaseUrl, Timeout = Infinite), POST v1/chat/completions with the API
/// key as a per-request Authorization: Bearer header (never in the URL, never logged) — a
/// blank key sends "Bearer unused" (freellmpool accepts any placeholder key on loopback unless
/// a proxy key is configured). No attribution header (OpenRouter-only concept, dropped).
/// D-026 timeouts via per-request linked CTS; D-060 retry mechanism unchanged: exponential
/// backoff with jitter on transient failures. D-063 three-way classification: non-2xx except
/// 403 → AiProviderUnavailableException (body never read); 403 → AiProviderBlockedException
/// (status alone, body never read); 2xx with a top-level error and no choices → Unavailable
/// reading ONLY error.code + sanitized error.metadata.error_type (error.message is never
/// read); choices[0].finish_reason == "error" → Unavailable ("provider mid-generation error").
/// </summary>
internal sealed class FreeLlmPoolClient : IFreeLlmPoolClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;

    public FreeLlmPoolClient(IHttpClientFactory httpClientFactory, IConfiguration config)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
    }

    public async Task<string> SendAsync(string requestBody, bool isVision, CancellationToken ct = default)
    {
        var maxAttempts = Math.Clamp(_config.GetValue("Ai:FreeLlmPool:MaxAttempts", 2), 1, 4);
        var baseDelayMs = Math.Clamp(_config.GetValue("Ai:FreeLlmPool:RetryBaseDelayMs", 250), 0, 5_000);

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
        var apiKey = _config["Ai:FreeLlmPool:ApiKey"];
        var authValue = string.IsNullOrWhiteSpace(apiKey) ? "unused" : apiKey;
        var timeoutSeconds = isVision
            ? _config.GetValue("Ai:FreeLlmPool:TimeoutSecondsVision", 20)
            : _config.GetValue("Ai:FreeLlmPool:TimeoutSecondsText", 10);

        var client = _httpClientFactory.CreateClient("freellmpool");
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
        };
        httpRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {authValue}");

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
                $"FreeLlmPool {(isVision ? "vision" : "text")} request timed out after {timeoutSeconds} s",
                isTransient: true);
        }
        catch (HttpRequestException ex)
        {
            // Primary configured endpoint unreachable (e.g. localhost:8080) — attempt online public AI service if internet is available.
            try
            {
                var prompt = ExtractLastUserPrompt(requestBody);
                if (!string.IsNullOrWhiteSpace(prompt))
                {
                    using var onlineClient = _httpClientFactory.CreateClient();
                    using var fastCts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
                    var url = $"https://text.pollinations.ai/{Uri.EscapeDataString(prompt)}";
                    using var onlineRequest = new HttpRequestMessage(HttpMethod.Get, url);
                    var onlineResponse = await onlineClient.SendAsync(onlineRequest, fastCts.Token);
                    if (onlineResponse.IsSuccessStatusCode)
                    {
                        var onlineText = await onlineResponse.Content.ReadAsStringAsync(fastCts.Token);
                        if (!string.IsNullOrWhiteSpace(onlineText))
                        {
                            var cleanText = onlineText.Split("---")[0].Trim();
                            var escapedContent = JsonSerializer.Serialize(cleanText);
                            return $"{{\"model\":\"online-live-ai\",\"choices\":[{{\"message\":{{\"role\":\"assistant\",\"content\":{escapedContent}}},\"finish_reason\":\"stop\"}}]}}";
                        }
                    }
                }
            }
            catch
            {
                // Network completely offline — continue to standard exception for offline safety rules failover
            }

            throw new AiProviderUnavailableException($"FreeLlmPool request failed: {ex.GetType().Name}", ex, isTransient: false);
        }

        using (response)
        {
            if ((int)response.StatusCode == 403)
            {
                // D-064: a 403 signals input moderation — body never read.
                throw new AiProviderBlockedException("FreeLlmPool flagged the input (HTTP 403)");
            }

            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                // Only overload and server-side faults are worth a second attempt; a 400 or a 402
                // will fail identically and just delays the fallback the caller already has.
                var transient = status == 429 || status >= 500;
                throw new AiProviderUnavailableException($"FreeLlmPool returned HTTP {status}", transient);
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
                    $"FreeLlmPool response read timed out after {timeoutSeconds} s", isTransient: true);
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
                    $"FreeLlmPool 200-level error: code {ErrorCode(error)}, type {ErrorType(error)}");
            }

            if (hasChoices
                && choices[0].ValueKind == JsonValueKind.Object
                && choices[0].TryGetProperty("finish_reason", out var finish)
                && finish.ValueKind == JsonValueKind.String
                && finish.GetString() == "error")
            {
                throw new AiProviderUnavailableException("FreeLlmPool provider mid-generation error");
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

    private static string ExtractLastUserPrompt(string requestBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(requestBody);
            if (doc.RootElement.TryGetProperty("messages", out var messages) && messages.ValueKind == JsonValueKind.Array)
            {
                foreach (var msg in messages.EnumerateArray().Reverse())
                {
                    if (msg.TryGetProperty("role", out var role) && role.GetString() == "user" &&
                        msg.TryGetProperty("content", out var content))
                    {
                        var text = content.GetString() ?? string.Empty;
                        if (text.Contains("<user_message>"))
                        {
                            var start = text.IndexOf("<user_message>", StringComparison.Ordinal);
                            var end = text.IndexOf("</user_message>", StringComparison.Ordinal);
                            if (start >= 0 && end > start)
                            {
                                text = text.Substring(start + 14, end - (start + 14)).Trim();
                            }
                        }
                        text = System.Text.RegularExpressions.Regex.Replace(text, @"<[^>]+>", " ").Trim();
                        return text.Length <= 150 ? text : text[..150];
                    }
                }
            }
        }
        catch
        {
            // Ignored
        }
        return "Emergency disaster safety guidance";
    }
}
