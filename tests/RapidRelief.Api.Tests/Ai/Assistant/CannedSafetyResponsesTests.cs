using RapidRelief.Api.Features.Ai.Assistant;

namespace RapidRelief.Api.Tests.Ai.Assistant;

/// <summary>
/// F16 TEST PLAN item 12 — D-053: the canned taxonomy must be deterministic (a demo watching
/// the same query twice must not see two answers) and every text must be demo-safe.
/// </summary>
public sealed class CannedSafetyResponsesTests
{
    [Theory]
    [InlineData("the earthquake shook our house", "Earthquake")]
    [InlineData("there was a tremor just now", "Earthquake")]
    [InlineData("my neighbour is trapped under rubble", "BuildingCollapse")]
    [InlineData("the roof collapse happened an hour ago", "BuildingCollapse")]
    [InlineData("a cyclone is coming tonight", "Cyclone")]
    [InlineData("the storm surge is rising", "Cyclone")]
    [InlineData("there is a landslide on the hill road", "Landslide")]
    [InlineData("mudslide blocked our street", "Landslide")]
    [InlineData("there is smoke in the stairwell", "Fire")]
    [InlineData("my kitchen is burning", "Fire")]
    [InlineData("flood water entered the ground floor", "Flood")]
    [InlineData("the road is waterlogged", "Flood")]
    [InlineData("what should I keep in an emergency bag", "General")]
    [InlineData("", "General")]
    public void Keywords_select_the_documented_category(string question, string expected)
    {
        Assert.Equal(expected, CannedSafetyResponses.CategoryFor(question).ToString());
    }

    [Fact]
    public void Category_matching_is_case_insensitive()
    {
        Assert.Equal(CannedCategory.Fire, CannedSafetyResponses.CategoryFor("THERE IS A FIRE"));
    }

    [Fact]
    public void An_ambiguous_question_resolves_to_the_same_category_every_time()
    {
        // "fire near the flooded road" matches two categories — the pinned scan order decides.
        var categories = Enumerable.Range(0, 100)
            .Select(_ => CannedSafetyResponses.CategoryFor("fire near the flooded road"))
            .Distinct()
            .ToList();

        Assert.Equal(new[] { CannedCategory.Fire }, categories);
    }

    [Fact]
    public void The_scan_order_is_earthquake_collapse_cyclone_landslide_fire_flood_then_general()
    {
        Assert.Equal(
            new[]
            {
                CannedCategory.Earthquake, CannedCategory.BuildingCollapse, CannedCategory.Cyclone,
                CannedCategory.Landslide, CannedCategory.Fire, CannedCategory.Flood, CannedCategory.General,
            },
            Enum.GetValues<CannedCategory>());
    }

    [Theory]
    [InlineData("earthquake shaking")]
    [InlineData("trapped in rubble")]
    [InlineData("cyclone warning")]
    [InlineData("landslide on the hill")]
    [InlineData("fire and smoke")]
    [InlineData("flood water rising")]
    [InlineData("nothing in particular")]
    public void Every_canned_text_is_short_plain_and_tells_the_user_to_call_999(string question)
    {
        var text = CannedSafetyResponses.TextFor(question);

        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Contains("999", text, StringComparison.Ordinal);
        Assert.True(text.Split('\n').Length <= 6, "canned answers are at most 6 short lines");
        Assert.DoesNotContain("http", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("www.", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<", text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_answer_wrapper_reports_the_canned_provider_and_no_model_telemetry()
    {
        var answer = CannedSafetyResponses.For("flood water rising", latencyMs: 12);

        Assert.Equal("Canned", answer.Provider);
        Assert.Equal(CannedSafetyResponses.TextFor("flood water rising"), answer.Text);
        Assert.False(answer.Truncated);
        Assert.Equal(12, answer.LatencyMs);
        Assert.Null(answer.TokensUsed);
        Assert.Null(answer.FinishReason);
    }
}
