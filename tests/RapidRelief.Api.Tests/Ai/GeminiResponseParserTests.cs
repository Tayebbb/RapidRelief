using System.Text.Json;
using RapidRelief.Api.Features.Ai.Gemini;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Tests.Ai;

/// <summary>
/// F8 blueprint TEST PLAN items 2–3 (parser half): closed-schema validation — exact enum
/// names, severity 1–5, STOP-only finishReason, clamp-not-reject for summary/confidence.
/// </summary>
public sealed class GeminiResponseParserTests
{
    /// <summary>Builds a canned generateContent response body around the inner assessment JSON.</summary>
    private static string Body(string innerJson, string finishReason = "STOP", int? totalTokenCount = 57)
    {
        var usage = totalTokenCount is { } tokens
            ? $",\"usageMetadata\":{{\"totalTokenCount\":{tokens}}}"
            : string.Empty;
        return $"{{\"candidates\":[{{\"content\":{{\"parts\":[{{\"text\":{JsonSerializer.Serialize(innerJson)}}}]}},\"finishReason\":{JsonSerializer.Serialize(finishReason)}}}]{usage}}}";
    }

    private static string Inner(string predictedType = "Fire", string severity = "4",
        string summary = "Warehouse fire with heavy smoke.", string confidence = "0.9")
        => $"{{\"predictedType\":\"{predictedType}\",\"severity\":{severity},\"summary\":{JsonSerializer.Serialize(summary)},\"confidence\":{confidence}}}";

    [Fact]
    public void Valid_response_parses_with_all_fields()
    {
        var ok = GeminiResponseParser.TryParse(Body(Inner()), out var parsed, out var reason);

        Assert.True(ok, reason);
        Assert.NotNull(parsed);
        Assert.Equal(DisasterType.Fire, parsed!.PredictedType);
        Assert.Equal(4, parsed.Severity);
        Assert.Equal("Warehouse fire with heavy smoke.", parsed.Summary);
        Assert.Equal(0.9, parsed.Confidence);
        Assert.Equal("STOP", parsed.FinishReason);
        Assert.Equal(57, parsed.TotalTokenCount);
    }

    [Fact]
    public void Missing_usage_metadata_still_parses_with_null_tokens()
    {
        var ok = GeminiResponseParser.TryParse(Body(Inner(), totalTokenCount: null), out var parsed, out _);

        Assert.True(ok);
        Assert.Null(parsed!.TotalTokenCount);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("{\"candidates\":[]}")]
    [InlineData("{\"candidates\":[{\"content\":{\"parts\":[]},\"finishReason\":\"STOP\"}]}")]
    public void Malformed_outer_body_is_rejected(string body)
    {
        Assert.False(GeminiResponseParser.TryParse(body, out _, out _));
    }

    [Fact]
    public void Inner_text_that_is_not_json_is_rejected()
    {
        Assert.False(GeminiResponseParser.TryParse(Body("I think this is a fire."), out _, out _));
    }

    [Theory]
    [InlineData("fire")]     // wrong case — Enum.TryParse must be case-sensitive
    [InlineData("Tsunami")]  // not in the closed enum
    [InlineData("3")]        // numeric strings must not sneak through Enum.TryParse
    public void Invalid_predicted_type_is_rejected(string predictedType)
    {
        Assert.False(GeminiResponseParser.TryParse(Body(Inner(predictedType: predictedType)), out _, out var reason));
        Assert.NotNull(reason);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("6")]
    [InlineData("7")]
    [InlineData("3.5")]
    public void Out_of_range_or_non_integer_severity_is_rejected(string severity)
    {
        Assert.False(GeminiResponseParser.TryParse(Body(Inner(severity: severity)), out _, out _));
    }

    [Theory]
    [InlineData("MAX_TOKENS")]
    [InlineData("SAFETY")]
    public void Non_stop_finish_reason_is_rejected(string finishReason)
    {
        Assert.False(GeminiResponseParser.TryParse(Body(Inner(), finishReason: finishReason), out _, out _));
    }

    [Fact]
    public void Missing_required_field_is_rejected()
    {
        const string noSummary = "{\"predictedType\":\"Fire\",\"severity\":4,\"confidence\":0.9}";

        Assert.False(GeminiResponseParser.TryParse(Body(noSummary), out _, out _));
    }

    [Fact]
    public void Overlong_summary_is_truncated_to_200_not_rejected()
    {
        var longSummary = new string('x', 250);

        var ok = GeminiResponseParser.TryParse(Body(Inner(summary: longSummary)), out var parsed, out _);

        Assert.True(ok);
        Assert.Equal(200, parsed!.Summary.Length);
        Assert.Equal(new string('x', 200), parsed.Summary);
    }

    [Fact]
    public void Control_characters_are_stripped_from_the_summary()
    {
        var ok = GeminiResponseParser.TryParse(
            Body(Inner(summary: "line1\nline2\ttab\u0000end")), out var parsed, out _);

        Assert.True(ok);
        Assert.Equal("line1line2tabend", parsed!.Summary);
        Assert.DoesNotContain(parsed.Summary, char.IsControl);
    }

    [Theory]
    [InlineData("1.7", 1.0)]
    [InlineData("-0.5", 0.0)]
    public void Out_of_range_confidence_is_clamped_not_rejected(string confidence, double expected)
    {
        var ok = GeminiResponseParser.TryParse(Body(Inner(confidence: confidence)), out var parsed, out _);

        Assert.True(ok);
        Assert.Equal(expected, parsed!.Confidence);
    }

    [Fact]
    public void Full_realistic_v1beta_response_shape_parses_with_extra_fields_ignored()
    {
        // Real generateContent envelope (chunk-2 confirmation): content.role, avgLogprobs,
        // modelVersion, responseId, and the full usageMetadata breakdown must all be tolerated;
        // only candidates[0].content.parts[0].text + finishReason + totalTokenCount are consumed.
        const string realBody = """
            {
              "candidates": [
                {
                  "content": {
                    "parts": [
                      { "text": "{\"predictedType\":\"Flood\",\"severity\":4,\"summary\":\"Extensive urban flooding with residents trapped.\",\"confidence\":0.86}" }
                    ],
                    "role": "model"
                  },
                  "finishReason": "STOP",
                  "avgLogprobs": -0.0123
                }
              ],
              "usageMetadata": {
                "promptTokenCount": 181,
                "candidatesTokenCount": 38,
                "totalTokenCount": 219,
                "promptTokensDetails": [ { "modality": "TEXT", "tokenCount": 181 } ]
              },
              "modelVersion": "gemini-3.7-flash",
              "responseId": "abc123XYZ"
            }
            """;

        var ok = GeminiResponseParser.TryParse(realBody, out var parsed, out var reason);

        Assert.True(ok, reason);
        Assert.Equal(DisasterType.Flood, parsed!.PredictedType);
        Assert.Equal(4, parsed.Severity);
        Assert.Equal("Extensive urban flooding with residents trapped.", parsed.Summary);
        Assert.Equal(0.86, parsed.Confidence);
        Assert.Equal("STOP", parsed.FinishReason);
        Assert.Equal(219, parsed.TotalTokenCount);
    }

    [Fact]
    public void Inner_json_split_across_two_text_parts_is_concatenated_and_parsed()
    {
        // Gemini may split long output across several text parts — all must be joined.
        var inner = Inner();
        var firstHalf = inner[..20];
        var secondHalf = inner[20..];
        var body = "{\"candidates\":[{\"content\":{\"parts\":["
            + $"{{\"text\":{JsonSerializer.Serialize(firstHalf)}}},{{\"text\":{JsonSerializer.Serialize(secondHalf)}}}"
            + "]},\"finishReason\":\"STOP\"}]}";

        var ok = GeminiResponseParser.TryParse(body, out var parsed, out var reason);

        Assert.True(ok, reason);
        Assert.Equal(DisasterType.Fire, parsed!.PredictedType);
        Assert.Equal(4, parsed.Severity);
        Assert.Equal("Warehouse fire with heavy smoke.", parsed.Summary);
    }

    [Fact]
    public void Non_text_parts_are_skipped_while_text_parts_still_concatenate()
    {
        var inner = Inner();
        var body = "{\"candidates\":[{\"content\":{\"parts\":["
            + $"{{\"inlineData\":{{\"mimeType\":\"image/png\",\"data\":\"AAAA\"}}}},{{\"text\":{JsonSerializer.Serialize(inner)}}}"
            + "]},\"finishReason\":\"STOP\"}]}";

        var ok = GeminiResponseParser.TryParse(body, out var parsed, out var reason);

        Assert.True(ok, reason);
        Assert.Equal(DisasterType.Fire, parsed!.PredictedType);
    }

    [Fact]
    public void Hostile_finish_reason_is_sanitized_and_clamped_in_the_reject_reason()
    {
        var hostile = "EVIL\r\nFAKE-LOG <script>alert(1)</script> " + new string('A', 100);

        var ok = GeminiResponseParser.TryParse(Body(Inner(), finishReason: hostile), out _, out var reason);

        Assert.False(ok);
        Assert.NotNull(reason);
        Assert.DoesNotContain('\n', reason!); // no log-line injection
        Assert.DoesNotContain('\r', reason);
        Assert.DoesNotContain('<', reason);
        Assert.DoesNotContain(hostile, reason);
        // 32 chars of [A-Za-z0-9_] only.
        Assert.Contains("'EVILFAKELOGscriptalert1scriptAAA'", reason);
    }
}
