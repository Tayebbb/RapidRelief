using System.Text.Json;

namespace RapidRelief.Api.Features.Ai.Assistant;
/// <summary>
/// D-050 prose finish policy. <see cref="Blocked"/> is a normal, user-visible outcome that
/// must NOT count against the shared circuit breaker; <see cref="Invalid"/> is a structural
/// failure of the provider path and does.
/// </summary>
internal enum AssistantReadStatus
{
    Ok,
    Blocked,
    Invalid,
}

internal sealed record AssistantReadResult(
    AssistantReadStatus Status,
    string Text,
    bool Truncated,
    string? FinishReason,
    int? TotalTokenCount,
    string? ModelName,
    string? Reason);

/// <summary>
/// Extracts prose from a chat-completions response (choices[0].message.content, string-only
/// stance) and applies the D-050 policy with the D-064 OpenRouter signals: "stop" → Ok,
/// "length" → Ok + truncated, "content_filter" → Blocked, "error" → Invalid (client
/// backstop — counts), missing/other → Blocked, no choices → Invalid. Captures
/// usage.total_tokens and response.model (D-061).
/// </summary>
internal static class AssistantResponseReader
{
    public static AssistantReadResult Read(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return Invalid("response body is empty");
        }

        JsonDocument outer;
        try
        {
            outer = JsonDocument.Parse(responseBody);
        }
        catch (JsonException)
        {
            return Invalid("response body is not valid JSON");
        }

        using (outer)
        {
            var root = outer.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Invalid("response body is not a JSON object");
            }

            // A bare 200 with no choices is a proxy/quota failure (the error-envelope case
            // already threw in the client): calling it "blocked" would never count against
            // the breaker and would keep re-arming probes forever.
            if (!root.TryGetProperty("choices", out var choices)
                || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
            {
                return Invalid("response has no choices");
            }

            var choice = choices[0];
            if (choice.ValueKind != JsonValueKind.Object)
            {
                return Invalid("choices[0] is not an object");
            }

            var finishReason = choice.TryGetProperty("finish_reason", out var finishElement)
                               && finishElement.ValueKind == JsonValueKind.String
                ? finishElement.GetString()!
                : string.Empty;

            if (finishReason == "error")
            {
                // D-063 backstop: a provider mid-generation failure counts against the breaker.
                return Invalid("finish_reason was 'error' (provider mid-generation failure)");
            }

            // D-050: truncated prose is still useful, unlike F8's truncated JSON.
            var truncated = finishReason == "length";
            if (finishReason != "stop" && !truncated)
            {
                return Blocked($"finish_reason was '{Sanitize(finishReason)}'");
            }

            // String-only stance: docs guarantee a string for non-streaming responses.
            if (!choice.TryGetProperty("message", out var message)
                || message.ValueKind != JsonValueKind.Object
                || !message.TryGetProperty("content", out var contentElement)
                || contentElement.ValueKind != JsonValueKind.String)
            {
                return Invalid("choices[0].message.content is missing or not a string");
            }

            int? totalTokenCount = null;
            if (root.TryGetProperty("usage", out var usage)
                && usage.ValueKind == JsonValueKind.Object
                && usage.TryGetProperty("total_tokens", out var tokens)
                && tokens.ValueKind == JsonValueKind.Number
                && tokens.TryGetInt32(out var tokenCount))
            {
                totalTokenCount = tokenCount;
            }

            // D-061: the ACTUAL routed model, not the config echo.
            var modelName = root.TryGetProperty("model", out var model)
                            && model.ValueKind == JsonValueKind.String
                ? model.GetString()
                : null;

            return new AssistantReadResult(AssistantReadStatus.Ok, contentElement.GetString()!, truncated,
                finishReason, totalTokenCount, modelName, Reason: null);
        }
    }

    private static AssistantReadResult Blocked(string reason)
        => new(AssistantReadStatus.Blocked, string.Empty, false, null, null, null, reason);

    private static AssistantReadResult Invalid(string reason)
        => new(AssistantReadStatus.Invalid, string.Empty, false, null, null, null, reason);

    /// <summary>Strips to [A-Za-z0-9_] and clamps to 32 chars — safe to embed in log messages.</summary>
    private static string Sanitize(string raw)
    {
        var cleaned = new string(raw.Where(c => char.IsAsciiLetterOrDigit(c) || c == '_').ToArray());
        return cleaned.Length <= 32 ? cleaned : cleaned[..32];
    }
}
