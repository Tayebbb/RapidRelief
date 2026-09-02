using System.Text.Json;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Features.Ai.OpenRouter;

/// <summary>Validated assessment extracted from an OpenRouter chat-completions response.</summary>
internal sealed record ParsedAssessment(
    DisasterType PredictedType,
    int Severity,
    string Summary,
    double Confidence,
    string FinishReason,
    int? TotalTokenCount,
    string? ModelName);

/// <summary>
/// D-064 tri-state parse outcome. <see cref="Blocked"/> is a normal, user-visible outcome
/// that must NOT count against the shared circuit breaker; <see cref="Invalid"/> is a
/// structural failure of the provider path and does.
/// </summary>
internal enum AiParseStatus
{
    Ok,
    Blocked,
    Invalid,
}

internal sealed record AiParseResult(AiParseStatus Status, ParsedAssessment? Parsed, string? RejectReason);

/// <summary>
/// Parses choices[0].message.content (string-only stance) as inner JSON, then validates
/// against the closed response schema: exact enum names (case-sensitive), severity 1–5,
/// summary clamped to 200 chars with control chars stripped, confidence clamped 0–1
/// (log-only downstream). finish_reason: "stop" → validate; "length" → Invalid (truncated
/// JSON is useless); "content_filter" → Blocked; "error" → Invalid (client backstop);
/// missing/other → Invalid. Captures usage.total_tokens and response.model (D-061).
/// </summary>
internal static class OpenRouterResponseParser
{
    private const int MaxSummaryLength = 200;

    public static AiParseResult Parse(string responseBody)
    {
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

            // No choices without a top-level error = Invalid (counts); the error+no-choices
            // case never reaches this parser — the client already threw (D-063).
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

            if (finishReason == "content_filter")
            {
                // D-064: a moderation verdict is Blocked — never a breaker count.
                return new AiParseResult(AiParseStatus.Blocked, null, "finish_reason was 'content_filter'");
            }

            if (finishReason != "stop")
            {
                // Log hygiene: the model-controlled value is clamped/stripped before it can
                // reach any log line via the reject reason.
                return Invalid($"finish_reason was '{SanitizeForMessage(finishReason)}', expected stop");
            }

            // String-only stance: docs guarantee a string for non-streaming responses.
            if (!choice.TryGetProperty("message", out var message)
                || message.ValueKind != JsonValueKind.Object
                || !message.TryGetProperty("content", out var contentElement)
                || contentElement.ValueKind != JsonValueKind.String)
            {
                return Invalid("choices[0].message.content is missing or not a string");
            }
            var innerText = contentElement.GetString()!;

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

            return ValidateInner(innerText, finishReason, totalTokenCount, modelName);
        }
    }

    private static AiParseResult ValidateInner(
        string innerText, string finishReason, int? totalTokenCount, string? modelName)
    {
        JsonDocument inner;
        try
        {
            inner = JsonDocument.Parse(innerText);
        }
        catch (JsonException)
        {
            return Invalid("inner text is not valid JSON");
        }

        using (inner)
        {
            var root = inner.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Invalid("inner JSON is not an object");
            }

            // Closed enum: exact names only, case-sensitive — numeric strings must not
            // sneak through Enum.TryParse.
            if (!root.TryGetProperty("predictedType", out var typeElement)
                || typeElement.ValueKind != JsonValueKind.String
                || !Enum.GetNames<DisasterType>().Contains(typeElement.GetString(), StringComparer.Ordinal))
            {
                return Invalid("predictedType is missing or not a known DisasterType name");
            }
            var predictedType = Enum.Parse<DisasterType>(typeElement.GetString()!);

            if (!root.TryGetProperty("severity", out var severityElement)
                || severityElement.ValueKind != JsonValueKind.Number
                || !severityElement.TryGetInt32(out var severity)
                || severity is < 1 or > 5)
            {
                return Invalid("severity is missing or outside 1–5");
            }

            if (!root.TryGetProperty("summary", out var summaryElement)
                || summaryElement.ValueKind != JsonValueKind.String)
            {
                return Invalid("summary is missing");
            }
            var summary = ClampSummary(summaryElement.GetString()!);

            if (!root.TryGetProperty("confidence", out var confidenceElement)
                || confidenceElement.ValueKind != JsonValueKind.Number)
            {
                return Invalid("confidence is missing");
            }
            var confidence = Math.Clamp(confidenceElement.GetDouble(), 0.0, 1.0);

            var parsed = new ParsedAssessment(predictedType, severity, summary, confidence,
                finishReason, totalTokenCount, modelName);
            return new AiParseResult(AiParseStatus.Ok, parsed, RejectReason: null);
        }
    }

    private static AiParseResult Invalid(string reason)
        => new(AiParseStatus.Invalid, Parsed: null, reason);

    private static string ClampSummary(string raw)
    {
        var cleaned = new string(raw.Where(c => !char.IsControl(c)).ToArray());
        return cleaned.Length <= MaxSummaryLength ? cleaned : cleaned[..MaxSummaryLength];
    }

    /// <summary>Strips to [A-Za-z0-9_] and clamps to 32 chars — safe to embed in log messages.</summary>
    private static string SanitizeForMessage(string raw)
    {
        var cleaned = new string(raw.Where(c => char.IsAsciiLetterOrDigit(c) || c == '_').ToArray());
        return cleaned.Length <= 32 ? cleaned : cleaned[..32];
    }
}
