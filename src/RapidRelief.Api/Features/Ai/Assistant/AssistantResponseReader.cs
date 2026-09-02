using System.Text;
using System.Text.Json;

namespace RapidRelief.Api.Features.Ai.Assistant;

/// <summary>
/// D-050 prose finish policy. <see cref="Blocked"/> is a normal, user-visible outcome that
/// must NOT count against the shared circuit breaker; <see cref="Invalid"/> is a structural
/// failure of the Gemini path and does.
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
    string? Reason);

/// <summary>Extracts prose from a generateContent response and applies the D-050 policy.</summary>
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

            if (root.TryGetProperty("promptFeedback", out var feedback)
                && feedback.ValueKind == JsonValueKind.Object
                && feedback.TryGetProperty("blockReason", out var blockReason)
                && blockReason.ValueKind == JsonValueKind.String)
            {
                return Blocked($"promptFeedback.blockReason was '{Sanitize(blockReason.GetString()!)}'");
            }

            if (!root.TryGetProperty("candidates", out var candidates)
                || candidates.ValueKind != JsonValueKind.Array
                || candidates.GetArrayLength() == 0)
            {
                // Only a response that carries a promptFeedback verdict is a provider-side block.
                // A bare candidate-less 200 is a proxy/quota failure: calling it "blocked" would
                // never count against the breaker and would keep re-arming probes forever.
                return root.TryGetProperty("promptFeedback", out var verdict)
                       && verdict.ValueKind == JsonValueKind.Object
                    ? Blocked("response has no candidates")
                    : Invalid("response has no candidates");
            }

            var candidate = candidates[0];
            var finishReason = candidate.TryGetProperty("finishReason", out var finishElement)
                               && finishElement.ValueKind == JsonValueKind.String
                ? finishElement.GetString()!
                : string.Empty;

            // D-050: truncated prose is still useful, unlike F8's truncated JSON.
            var truncated = finishReason == "MAX_TOKENS";
            if (finishReason != "STOP" && !truncated)
            {
                return Blocked($"finishReason was '{Sanitize(finishReason)}'");
            }

            if (!candidate.TryGetProperty("content", out var content)
                || !content.TryGetProperty("parts", out var parts)
                || parts.ValueKind != JsonValueKind.Array)
            {
                return Invalid("candidates[0].content.parts is missing");
            }

            var textBuilder = new StringBuilder();
            var sawText = false;
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var textElement)
                    && textElement.ValueKind == JsonValueKind.String)
                {
                    sawText = true;
                    textBuilder.Append(textElement.GetString());
                }
            }

            if (!sawText)
            {
                return Invalid("candidates[0].content.parts contains no text");
            }

            int? totalTokenCount = null;
            if (root.TryGetProperty("usageMetadata", out var usage)
                && usage.ValueKind == JsonValueKind.Object
                && usage.TryGetProperty("totalTokenCount", out var tokens)
                && tokens.ValueKind == JsonValueKind.Number
                && tokens.TryGetInt32(out var tokenCount))
            {
                totalTokenCount = tokenCount;
            }

            return new AssistantReadResult(AssistantReadStatus.Ok, textBuilder.ToString(), truncated,
                finishReason, totalTokenCount, Reason: null);
        }
    }

    private static AssistantReadResult Blocked(string reason)
        => new(AssistantReadStatus.Blocked, string.Empty, false, null, null, reason);

    private static AssistantReadResult Invalid(string reason)
        => new(AssistantReadStatus.Invalid, string.Empty, false, null, null, reason);

    /// <summary>Strips to [A-Za-z0-9_] and clamps to 32 chars — safe to embed in log messages.</summary>
    private static string Sanitize(string raw)
    {
        var cleaned = new string(raw.Where(c => char.IsAsciiLetterOrDigit(c) || c == '_').ToArray());
        return cleaned.Length <= 32 ? cleaned : cleaned[..32];
    }
}
