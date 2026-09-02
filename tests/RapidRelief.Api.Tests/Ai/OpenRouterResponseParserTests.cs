using System.Text.Json;
using RapidRelief.Api.Features.Ai.OpenRouter;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Tests.Ai;

/// <summary>
/// D-063/D-064 (parser half): closed-schema validation over choices[0].message.content —
/// exact enum names, severity 1–5, clamp-not-reject for summary/confidence — plus the
/// tri-state finish policy: "stop" validates, "length" → Invalid (truncated JSON is useless),
/// "content_filter" → Blocked (never counts), "error" → Invalid (client backstop),
/// missing/other → Invalid. Captures usage.total_tokens and response.model (D-061).
/// </summary>
public sealed class OpenRouterResponseParserTests
{
    /// <summary>Builds a canned chat-completions response body around the inner assessment JSON.</summary>
    private static string Body(string innerJson, string finishReason = "stop", int? totalTokens = 57,
        string? model = "z-ai/glm-5.2:free")
    {
        var usage = totalTokens is { } tokens
            ? $",\"usage\":{{\"total_tokens\":{tokens}}}"
            : string.Empty;
        var modelField = model is null ? string.Empty : $"\"model\":{JsonSerializer.Serialize(model)},";
        return $"{{{modelField}\"choices\":[{{\"message\":{{\"role\":\"assistant\",\"content\":{JsonSerializer.Serialize(innerJson)}}},\"finish_reason\":{JsonSerializer.Serialize(finishReason)}}}]{usage}}}";
    }

    private static string Inner(string predictedType = "Fire", string severity = "4",
        string summary = "Warehouse fire with heavy smoke.", string confidence = "0.9")
        => $"{{\"predictedType\":\"{predictedType}\",\"severity\":{severity},\"summary\":{JsonSerializer.Serialize(summary)},\"confidence\":{confidence}}}";

    [Fact]
    public void Valid_response_parses_with_all_fields_including_the_routed_model()
    {
        var result = OpenRouterResponseParser.Parse(Body(Inner()));

        Assert.Equal(AiParseStatus.Ok, result.Status);
        Assert.NotNull(result.Parsed);
        Assert.Equal(DisasterType.Fire, result.Parsed!.PredictedType);
        Assert.Equal(4, result.Parsed.Severity);
        Assert.Equal("Warehouse fire with heavy smoke.", result.Parsed.Summary);
        Assert.Equal(0.9, result.Parsed.Confidence);
        Assert.Equal("stop", result.Parsed.FinishReason);
        Assert.Equal(57, result.Parsed.TotalTokenCount);
        Assert.Equal("z-ai/glm-5.2:free", result.Parsed.ModelName); // D-061: the ACTUAL routed model
    }

    [Fact]
    public void Missing_usage_and_model_still_parse_with_null_telemetry()
    {
        var result = OpenRouterResponseParser.Parse(Body(Inner(), totalTokens: null, model: null));

        Assert.Equal(AiParseStatus.Ok, result.Status);
        Assert.Null(result.Parsed!.TotalTokenCount);
        Assert.Null(result.Parsed.ModelName);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("{\"choices\":[]}")]
    [InlineData("[1,2,3]")]
    public void Malformed_or_choiceless_outer_body_is_invalid(string body)
    {
        var result = OpenRouterResponseParser.Parse(body);

        Assert.Equal(AiParseStatus.Invalid, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.RejectReason));
    }

    [Fact]
    public void A_non_object_first_choice_is_invalid_instead_of_throwing()
    {
        var result = OpenRouterResponseParser.Parse("{\"choices\":[123]}");

        Assert.Equal(AiParseStatus.Invalid, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.RejectReason));
    }

    [Theory]
    [InlineData("{\"choices\":[{\"finish_reason\":\"stop\"}]}")]
    [InlineData("{\"choices\":[{\"message\":{},\"finish_reason\":\"stop\"}]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":null},\"finish_reason\":\"stop\"}]}")]
    [InlineData("{\"choices\":[{\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"x\"}]},\"finish_reason\":\"stop\"}]}")]
    public void Missing_or_non_string_content_is_invalid(string body)
    {
        // String-only stance: docs guarantee a string for non-streaming; anything else counts.
        var result = OpenRouterResponseParser.Parse(body);

        Assert.Equal(AiParseStatus.Invalid, result.Status);
    }

    [Fact]
    public void Inner_text_that_is_not_json_is_invalid()
    {
        Assert.Equal(AiParseStatus.Invalid, OpenRouterResponseParser.Parse(Body("I think this is a fire.")).Status);
    }

    [Theory]
    [InlineData("fire")]     // wrong case — the closed enum is case-sensitive
    [InlineData("Tsunami")]  // not in the closed enum
    [InlineData("3")]        // numeric strings must not sneak through Enum.TryParse
    public void Invalid_predicted_type_is_invalid(string predictedType)
    {
        var result = OpenRouterResponseParser.Parse(Body(Inner(predictedType: predictedType)));

        Assert.Equal(AiParseStatus.Invalid, result.Status);
        Assert.NotNull(result.RejectReason);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("6")]
    [InlineData("7")]
    [InlineData("3.5")]
    public void Out_of_range_or_non_integer_severity_is_invalid(string severity)
    {
        Assert.Equal(AiParseStatus.Invalid, OpenRouterResponseParser.Parse(Body(Inner(severity: severity))).Status);
    }

    [Theory]
    [InlineData("length")]  // truncated JSON is useless — counts (D-063)
    [InlineData("error")]   // client backstop — counts
    [InlineData("STOP")]    // wrong case — unknown reason counts
    [InlineData("weird")]
    public void Non_stop_finish_reasons_are_invalid(string finishReason)
    {
        var result = OpenRouterResponseParser.Parse(Body(Inner(), finishReason: finishReason));

        Assert.Equal(AiParseStatus.Invalid, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.RejectReason));
    }

    [Fact]
    public void A_missing_finish_reason_is_invalid()
    {
        const string body = "{\"choices\":[{\"message\":{\"content\":\"{}\"}}]}";

        Assert.Equal(AiParseStatus.Invalid, OpenRouterResponseParser.Parse(body).Status);
    }

    [Fact]
    public void A_content_filter_finish_reason_is_blocked_not_invalid()
    {
        // D-064: a moderation verdict must NOT count against the shared breaker.
        var result = OpenRouterResponseParser.Parse(Body(Inner(), finishReason: "content_filter"));

        Assert.Equal(AiParseStatus.Blocked, result.Status);
        Assert.Null(result.Parsed);
        Assert.False(string.IsNullOrWhiteSpace(result.RejectReason));
    }

    [Fact]
    public void Missing_required_field_is_invalid()
    {
        const string noSummary = "{\"predictedType\":\"Fire\",\"severity\":4,\"confidence\":0.9}";

        Assert.Equal(AiParseStatus.Invalid, OpenRouterResponseParser.Parse(Body(noSummary)).Status);
    }

    [Fact]
    public void Overlong_summary_is_truncated_to_200_not_rejected()
    {
        var longSummary = new string('x', 250);

        var result = OpenRouterResponseParser.Parse(Body(Inner(summary: longSummary)));

        Assert.Equal(AiParseStatus.Ok, result.Status);
        Assert.Equal(200, result.Parsed!.Summary.Length);
        Assert.Equal(new string('x', 200), result.Parsed.Summary);
    }

    [Fact]
    public void Control_characters_are_stripped_from_the_summary()
    {
        var result = OpenRouterResponseParser.Parse(Body(Inner(summary: "line1\nline2\ttab\u0000end")));

        Assert.Equal(AiParseStatus.Ok, result.Status);
        Assert.Equal("line1line2tabend", result.Parsed!.Summary);
        Assert.DoesNotContain(result.Parsed.Summary, char.IsControl);
    }

    [Theory]
    [InlineData("1.7", 1.0)]
    [InlineData("-0.5", 0.0)]
    public void Out_of_range_confidence_is_clamped_not_rejected(string confidence, double expected)
    {
        var result = OpenRouterResponseParser.Parse(Body(Inner(confidence: confidence)));

        Assert.Equal(AiParseStatus.Ok, result.Status);
        Assert.Equal(expected, result.Parsed!.Confidence);
    }

    [Fact]
    public void Full_realistic_chat_completions_response_shape_parses_with_extra_fields_ignored()
    {
        // Real OpenRouter envelope: id/provider/created/object, native_finish_reason, logprobs
        // and the full usage breakdown must all be tolerated; only choices[0].message.content,
        // finish_reason, usage.total_tokens and model are consumed.
        const string realBody = """
            {
              "id": "gen-1234567890-AbCdEf",
              "provider": "Nvidia",
              "model": "nvidia/nemotron-3-super-120b-a12b:free",
              "object": "chat.completion",
              "created": 1767312000,
              "choices": [
                {
                  "logprobs": null,
                  "finish_reason": "stop",
                  "native_finish_reason": "stop",
                  "index": 0,
                  "message": {
                    "role": "assistant",
                    "content": "{\"predictedType\":\"Flood\",\"severity\":4,\"summary\":\"Extensive urban flooding with residents trapped.\",\"confidence\":0.86}",
                    "refusal": null,
                    "reasoning": null
                  }
                }
              ],
              "usage": {
                "prompt_tokens": 181,
                "completion_tokens": 38,
                "total_tokens": 219,
                "prompt_tokens_details": { "cached_tokens": 0 }
              }
            }
            """;

        var result = OpenRouterResponseParser.Parse(realBody);

        Assert.Equal(AiParseStatus.Ok, result.Status);
        Assert.Equal(DisasterType.Flood, result.Parsed!.PredictedType);
        Assert.Equal(4, result.Parsed.Severity);
        Assert.Equal("Extensive urban flooding with residents trapped.", result.Parsed.Summary);
        Assert.Equal(0.86, result.Parsed.Confidence);
        Assert.Equal("stop", result.Parsed.FinishReason);
        Assert.Equal(219, result.Parsed.TotalTokenCount);
        Assert.Equal("nvidia/nemotron-3-super-120b-a12b:free", result.Parsed.ModelName);
    }

    [Fact]
    public void Hostile_finish_reason_is_sanitized_and_clamped_in_the_reject_reason()
    {
        var hostile = "EVIL\r\nFAKE-LOG <script>alert(1)</script> " + new string('A', 100);

        var result = OpenRouterResponseParser.Parse(Body(Inner(), finishReason: hostile));

        Assert.Equal(AiParseStatus.Invalid, result.Status);
        Assert.NotNull(result.RejectReason);
        Assert.DoesNotContain('\n', result.RejectReason!); // no log-line injection
        Assert.DoesNotContain('\r', result.RejectReason);
        Assert.DoesNotContain('<', result.RejectReason);
        Assert.DoesNotContain(hostile, result.RejectReason);
        // 32 chars of [A-Za-z0-9_] only.
        Assert.Contains("'EVILFAKELOGscriptalert1scriptAAA'", result.RejectReason);
    }
}
