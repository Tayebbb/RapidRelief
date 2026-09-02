using System.Text.Json;
using RapidRelief.Api.Features.Ai.Assistant;

namespace RapidRelief.Api.Tests.Ai.Assistant;

/// <summary>
/// F16 TEST PLAN item 1 (reader half) — D-050 prose finish policy. The split between
/// <c>Blocked</c> (a normal user-visible outcome) and <c>Invalid</c> (a Gemini-path failure)
/// is what keeps three hostile messages from opening the shared breaker for everyone.
/// </summary>
public sealed class AssistantResponseReaderTests
{
    private static string Body(string text, string? finishReason = "STOP", int? tokens = 57)
    {
        var finish = finishReason is null ? "" : $",\"finishReason\":{JsonSerializer.Serialize(finishReason)}";
        var usage = tokens is null ? "" : $",\"usageMetadata\":{{\"totalTokenCount\":{tokens}}}";
        return $"{{\"candidates\":[{{\"content\":{{\"parts\":[{{\"text\":{JsonSerializer.Serialize(text)}}}]}}{finish}}}]{usage}}}";
    }

    [Fact]
    public void A_stop_response_is_accepted_untruncated_with_its_telemetry()
    {
        var result = AssistantResponseReader.Read(Body("Move to higher ground now."));

        Assert.Equal(AssistantReadStatus.Ok, result.Status);
        Assert.Equal("Move to higher ground now.", result.Text);
        Assert.False(result.Truncated);
        Assert.Equal("STOP", result.FinishReason);
        Assert.Equal(57, result.TotalTokenCount);
    }

    [Fact]
    public void A_max_tokens_response_is_accepted_but_marked_truncated()
    {
        // Truncated prose is still usable — the F8 "STOP only" rule would randomly can long answers.
        var result = AssistantResponseReader.Read(Body("Move to higher ground and", finishReason: "MAX_TOKENS"));

        Assert.Equal(AssistantReadStatus.Ok, result.Status);
        Assert.True(result.Truncated);
        Assert.Equal("MAX_TOKENS", result.FinishReason);
    }

    [Fact]
    public void All_text_parts_are_concatenated_in_order()
    {
        const string body = """
            {"candidates":[{"content":{"parts":[{"text":"first "},{"inlineData":{"data":"x"}},{"text":"second"}]},"finishReason":"STOP"}]}
            """;

        var result = AssistantResponseReader.Read(body);

        Assert.Equal(AssistantReadStatus.Ok, result.Status);
        Assert.Equal("first second", result.Text);
        Assert.Null(result.TotalTokenCount);
    }

    [Theory]
    [InlineData("SAFETY")]
    [InlineData("RECITATION")]
    [InlineData("OTHER")]
    [InlineData("PROHIBITED_CONTENT")]
    public void A_non_stop_non_max_tokens_finish_reason_is_blocked(string finishReason)
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
    public void A_prompt_feedback_block_reason_wins_over_everything_else()
    {
        const string body = """
            {"promptFeedback":{"blockReason":"SAFETY"},"candidates":[{"content":{"parts":[{"text":"ok"}]},"finishReason":"STOP"}]}
            """;

        var result = AssistantResponseReader.Read(body);

        Assert.Equal(AssistantReadStatus.Blocked, result.Status);
    }

    [Theory]
    [InlineData("""{"promptFeedback":{"blockReason":"OTHER"}}""")]
    [InlineData("""{"promptFeedback":{"safetyRatings":[]},"candidates":[]}""")]
    public void A_candidate_less_response_that_carries_prompt_feedback_is_blocked(string body)
    {
        // promptFeedback present ⇒ the provider judged the prompt: a normal, user-visible
        // outcome that must not open the breaker for everyone else.
        var result = AssistantResponseReader.Read(body);

        Assert.Equal(AssistantReadStatus.Blocked, result.Status);
    }

    [Theory]
    [InlineData("""{"candidates":[]}""")]
    [InlineData("{}")]
    [InlineData("""{"usageMetadata":{"totalTokenCount":0}}""")]
    [InlineData("""{"promptFeedback":"SAFETY"}""")]
    public void A_candidate_less_response_with_no_prompt_feedback_is_invalid_so_it_counts_against_the_breaker(
        string body)
    {
        // A bare 200 with no candidates and no verdict is a proxy/quota failure. Calling it
        // "blocked" would never count against the breaker and would keep re-arming probes.
        var result = AssistantResponseReader.Read(body);

        Assert.Equal(AssistantReadStatus.Invalid, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    [Theory]
    [InlineData("this is not json")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("""{"candidates":[{"finishReason":"STOP"}]}""")]
    [InlineData("""{"candidates":[{"content":{"parts":[{"inlineData":{"data":"x"}}]},"finishReason":"STOP"}]}""")]
    public void A_structurally_broken_response_is_invalid(string? body)
    {
        var result = AssistantResponseReader.Read(body);

        Assert.Equal(AssistantReadStatus.Invalid, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    [Fact]
    public void A_hostile_finish_reason_is_sanitised_before_it_can_reach_a_log_line()
    {
        var result = AssistantResponseReader.Read(Body("x", finishReason: "SAFETY\n[CRITICAL] fake log line"));

        Assert.Equal(AssistantReadStatus.Blocked, result.Status);
        Assert.NotNull(result.Reason);
        Assert.DoesNotContain("\n", result.Reason);
        Assert.DoesNotContain("[", result.Reason);
    }
}
