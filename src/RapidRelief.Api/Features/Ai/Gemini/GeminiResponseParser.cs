using System.Text;
using System.Text.Json;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Features.Ai.Gemini;

/// <summary>Validated assessment extracted from a Gemini generateContent response.</summary>
internal sealed record ParsedAssessment(
    DisasterType PredictedType,
    int Severity,
    string Summary,
    double Confidence,
    string FinishReason,
    int? TotalTokenCount);

/// <summary>
/// Parses candidates[0].content.parts[0].text as inner JSON, then validates against the
/// closed response schema: exact enum names (case-sensitive), severity 1–5, summary clamped
/// to 200 chars with control chars stripped, confidence clamped 0–1 (log-only downstream),
/// finishReason must be "STOP". Any structural violation → reject (composite falls back).
/// </summary>
internal static class GeminiResponseParser
{
    private const int MaxSummaryLength = 200;

    public static bool TryParse(string responseBody, out ParsedAssessment? parsed, out string? rejectReason)
    {
        parsed = null;

        if (!TryExtractCandidate(responseBody, out var innerText, out var finishReason, out var totalTokenCount, out rejectReason))
        {
            return false;
        }

        if (finishReason != "STOP")
        {
            // Log hygiene: the model-controlled value is clamped/stripped before it can reach
            // any log line via the reject reason.
            rejectReason = $"finishReason was '{SanitizeForMessage(finishReason)}', expected STOP";
            return false;
        }

        JsonDocument inner;
        try
        {
            inner = JsonDocument.Parse(innerText);
        }
        catch (JsonException)
        {
            rejectReason = "inner text is not valid JSON";
            return false;
        }

        using (inner)
        {
            var root = inner.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                rejectReason = "inner JSON is not an object";
                return false;
            }

            // Closed enum: exact names only, case-sensitive — numeric strings must not
            // sneak through Enum.TryParse.
            if (!root.TryGetProperty("predictedType", out var typeElement)
                || typeElement.ValueKind != JsonValueKind.String
                || !Enum.GetNames<DisasterType>().Contains(typeElement.GetString(), StringComparer.Ordinal))
            {
                rejectReason = "predictedType is missing or not a known DisasterType name";
                return false;
            }
            var predictedType = Enum.Parse<DisasterType>(typeElement.GetString()!);

            if (!root.TryGetProperty("severity", out var severityElement)
                || severityElement.ValueKind != JsonValueKind.Number
                || !severityElement.TryGetInt32(out var severity)
                || severity is < 1 or > 5)
            {
                rejectReason = "severity is missing or outside 1–5";
                return false;
            }

            if (!root.TryGetProperty("summary", out var summaryElement)
                || summaryElement.ValueKind != JsonValueKind.String)
            {
                rejectReason = "summary is missing";
                return false;
            }
            var summary = ClampSummary(summaryElement.GetString()!);

            if (!root.TryGetProperty("confidence", out var confidenceElement)
                || confidenceElement.ValueKind != JsonValueKind.Number)
            {
                rejectReason = "confidence is missing";
                return false;
            }
            var confidence = Math.Clamp(confidenceElement.GetDouble(), 0.0, 1.0);

            parsed = new ParsedAssessment(predictedType, severity, summary, confidence, finishReason, totalTokenCount);
            rejectReason = null;
            return true;
        }
    }

    private static bool TryExtractCandidate(
        string responseBody, out string innerText, out string finishReason,
        out int? totalTokenCount, out string? rejectReason)
    {
        innerText = string.Empty;
        finishReason = string.Empty;
        totalTokenCount = null;

        JsonDocument outer;
        try
        {
            outer = JsonDocument.Parse(responseBody);
        }
        catch (JsonException)
        {
            rejectReason = "response body is not valid JSON";
            return false;
        }

        using (outer)
        {
            var root = outer.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("candidates", out var candidates)
                || candidates.ValueKind != JsonValueKind.Array
                || candidates.GetArrayLength() == 0)
            {
                rejectReason = "response has no candidates";
                return false;
            }

            var candidate = candidates[0];
            if (candidate.TryGetProperty("finishReason", out var finishElement)
                && finishElement.ValueKind == JsonValueKind.String)
            {
                finishReason = finishElement.GetString()!;
            }

            // Gemini may split output across several parts — concatenate every string
            // "text" value in order; non-text parts (e.g. inlineData) are skipped.
            if (!candidate.TryGetProperty("content", out var content)
                || !content.TryGetProperty("parts", out var parts)
                || parts.ValueKind != JsonValueKind.Array)
            {
                rejectReason = "candidates[0].content.parts is missing";
                return false;
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
                rejectReason = "candidates[0].content.parts contains no text";
                return false;
            }
            innerText = textBuilder.ToString();

            if (root.TryGetProperty("usageMetadata", out var usage)
                && usage.ValueKind == JsonValueKind.Object
                && usage.TryGetProperty("totalTokenCount", out var tokens)
                && tokens.ValueKind == JsonValueKind.Number
                && tokens.TryGetInt32(out var tokenCount))
            {
                totalTokenCount = tokenCount;
            }

            rejectReason = null;
            return true;
        }
    }

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
