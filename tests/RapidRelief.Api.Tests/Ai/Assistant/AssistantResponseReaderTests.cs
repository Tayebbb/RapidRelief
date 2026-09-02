using System.Text.Json;
using RapidRelief.Api.Features.Ai.Assistant;

namespace RapidRelief.Api.Tests.Ai.Assistant;

/// <summary>
/// D-050 prose finish policy under the D-064 OpenRouter signals. The split between
/// <c>Blocked</c> (a normal user-visible outcome) and <c>Invalid</c> (a provider-path failure)
/// is what keeps three hostile messages from opening the shared breaker for everyone:
/// "stop" → Ok, "length" → Ok + truncated, "content_filter" → Blocked, "error" → Invalid
/// (client backstop — counts), missing/other → Blocked, no choices → Invalid.
/// </summary>
public sealed class AssistantResponseReaderTests
{
    private static string Body(string text, string? finishReason = "stop", int? tokens = 57,
        string? model = "z-ai/glm-5.2:free")
    {
        var finish = finishReason is null ? "" : $",\"finish_reason\":{JsonSerializer.Serialize(finishReason)}";
        var usage = tokens is null ? "" : $",\"usage\":{{\"total_tokens\":{tokens}}}";
        var modelField = model is null ? "" : $"\"model\":{JsonSerializer.Serialize(model)},";
        return $"{{{modelField}\"choices\":[{{\"message\":{{\"role\":\"assistant\",\"content\":{JsonSerializer.Serialize(text)}}}{finish}}}]{usage}}}";
    }

    [Fact]
    public void A_stop_response_is_accepted_untruncated_with_its_telemetry()
    {
        var result = AssistantResponseReader.Read(Body("Move to higher ground now."));

        Assert.Equal(AssistantReadStatus.Ok, result.Status);
        Assert.Equal("Move to higher ground now.", result.Text);
        Assert.False(result.Truncated);
        Assert.Equal("stop", result.FinishReason);
        Assert.Equal(57, result.TotalTokenCount);
        Assert.Equal("z-ai/glm-5.2:free", result.ModelName); // D-061: the ACTUAL routed model
    }

    [Fact]
    public void A_length_response_is_accepted_but_marked_truncated()
    {
        // Truncated prose is still usable — the F8 "stop only" rule would randomly can long answers.
        var result = AssistantResponseReader.Read(Body("Move to higher ground and", finishReason: "length"));

        Assert.Equal(AssistantReadStatus.Ok, result.Status);
        Assert.True(result.Truncated);
        Assert.Equal("length", result.FinishReason);
    }

    [Fact]
    public void Missing_usage_and_model_still_read_with_null_telemetry()
    {
        var result = AssistantResponseReader.Read(Body("ok", tokens: null, model: null));

        Assert.Equal(AssistantReadStatus.Ok, result.Status);
        Assert.Null(result.TotalTokenCount);
        Assert.Null(result.ModelName);
    }

    [Fact]
    public void A_content_filter_finish_reason_is_blocked()
    {
        // D-064: the moderation verdict rides on finish_reason now — never a breaker count.
        var result = AssistantResponseReader.Read(Body("partial", "content_filter"));

        Assert.Equal(AssistantReadStatus.Blocked, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    [Theory]
    [InlineData("STOP")]   // wrong case — unknown reason keeps today's Blocked posture
    [InlineData("recitation")]
    [InlineData("weird_reason")]
    public void An_unknown_finish_reason_is_blocked(string finishReason)
    {
        var result = AssistantResponseReader.Read(Body("partial", finishReason));

        Assert.Equal(AssistantReadStatus.Blocked, result.Status);
    }

    [Fact]
    public void A_missing_finish_reason_is_blocked()
    {
        var result = AssistantResponseReader.Read(Body("partial", finishReason: null));

        Assert.Equal(AssistantReadStatus.Blocked, result.Status);
    }

    [Fact]
    public void An_error_finish_reason_is_invalid_so_it_counts_against_the_breaker()
    {
        // D-063 backstop: a provider mid-generation error is an availability failure, not a block.
        var result = AssistantResponseReader.Read(Body("partial", "error"));

        Assert.Equal(AssistantReadStatus.Invalid, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    [Theory]
    [InlineData("""{"choices":[]}""")]
    [InlineData("{}")]
    [InlineData("""{"usage":{"total_tokens":0}}""")]
    public void A_choiceless_response_is_invalid_so_it_counts_against_the_breaker(string body)
    {
        // A bare 200 with no choices (the client already threw on the error-envelope case) is a
        // proxy/quota failure. Calling it "blocked" would never count and keep re-arming probes.
        var result = AssistantResponseReader.Read(body);

        Assert.Equal(AssistantReadStatus.Invalid, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    [Fact]
    public void A_non_object_first_choice_is_invalid_instead_of_throwing()
    {
        var result = AssistantResponseReader.Read("""{"choices":[123]}""");

        Assert.Equal(AssistantReadStatus.Invalid, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    [Theory]
    [InlineData("this is not json")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("[1,2,3]")]
    [InlineData("""{"choices":[{"finish_reason":"stop"}]}""")]
    [InlineData("""{"choices":[{"message":{},"finish_reason":"stop"}]}""")]
    [InlineData("""{"choices":[{"message":{"content":null},"finish_reason":"stop"}]}""")]
    [InlineData("""{"choices":[{"message":{"content":[{"type":"text","text":"x"}]},"finish_reason":"stop"}]}""")]
    public void A_structurally_broken_response_is_invalid(string? body)
    {
        // String-only stance: non-string/missing content counts (docs guarantee a string).
        var result = AssistantResponseReader.Read(body);

        Assert.Equal(AssistantReadStatus.Invalid, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    [Fact]
    public void A_hostile_finish_reason_is_sanitised_before_it_can_reach_a_log_line()
    {
        var result = AssistantResponseReader.Read(Body("x", finishReason: "content_filter\n[CRITICAL] fake log line"));

        Assert.Equal(AssistantReadStatus.Blocked, result.Status);
        Assert.NotNull(result.Reason);
        Assert.DoesNotContain("\n", result.Reason);
        Assert.DoesNotContain("[", result.Reason);
    }
}
