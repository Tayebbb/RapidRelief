using RapidRelief.Api.Features.Ai.Assistant;

namespace RapidRelief.Api.Tests.Ai.Assistant;

/// <summary>
/// F16 TEST PLAN item 7 — D-051 answer sanitization: the only defence available for free-text
/// output (no schema is possible), so the charset/length/link contract is pinned imperatively.
/// </summary>
public sealed class AssistantSanitizerTests
{
    private const int MaxLength = 1500;

    private static SanitizedAnswer Clean(string? raw) => AssistantSanitizer.Clean(raw, MaxLength);

    [Fact]
    public void Control_characters_are_stripped_but_newlines_and_tabs_survive()
    {
        var result = Clean("Move\u0000 to\u001b higher\tground.\nStay safe.");

        Assert.Equal("Move to higher\tground.\nStay safe.", result.Text);
        Assert.False(result.Empty);
    }

    [Fact]
    public void Windows_and_classic_mac_newlines_are_normalised_to_lf()
    {
        var result = Clean("first\r\nsecond\rthird");

        Assert.Equal("first\nsecond\nthird", result.Text);
    }

    [Theory]
    [InlineData("Visit https://evil.example/phish for help.", "Visit for help.")]
    [InlineData("Visit HTTP://EVIL.EXAMPLE now.", "Visit now.")]
    [InlineData("See www.evil.example/relief today.", "See today.")]
    public void Url_shaped_tokens_are_removed(string raw, string expected)
    {
        var result = Clean(raw);

        Assert.Equal(expected, result.Text);
        Assert.DoesNotContain("evil.example", result.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Register at ftp://relief.example/forms now.", "relief.example")]
    [InlineData("Open data:text/html;base64,PHNjcmlwdD4= now.", "base64")]
    [InlineData("Tap javascript:alert(1) to continue.", "javascript")]
    [InlineData("Write to mailto:help@relief.example today.", "mailto")]
    [InlineData("Call tel:+8801711234567 for aid.", "tel:")]
    [InlineData("Go to 192.168.10.5/relief for forms.", "192.168.10.5")]
    [InlineData("Apply at reliefbd.org before noon.", "reliefbd.org")]
    [InlineData("Apply at forms.reliefbd.com/apply before noon.", "reliefbd.com")]
    [InlineData("[Red Cross](https://redcross.example) can help.", "redcross.example")]
    public void Every_link_shaped_token_is_stripped_not_just_http_and_www(string raw, string forbidden)
    {
        var result = Clean(raw);

        Assert.DoesNotContain(forbidden, result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("://", result.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Call the relief hotline on 01711234567 for food.", "01711234567")]
    [InlineData("Call 0800-555-1212 for shelter places.", "0800-555-1212")]
    [InlineData("Dial +880 1711 234567 for rescue.", "234567")]
    public void Phone_shaped_digit_runs_are_stripped(string raw, string forbidden)
    {
        // A hallucinated hotline is a better phishing vector than a link in an emergency UI.
        var result = Clean(raw);

        Assert.DoesNotContain(forbidden, result.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Call 999 now and move to higher ground.")]
    [InlineData("If anyone is trapped, call 999 immediately.")]
    public void The_national_emergency_number_is_always_preserved(string raw)
    {
        Assert.Contains("999", Clean(raw).Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Take 5 litres of water per person for 3 days.")]
    [InlineData("Flood levels in 2026 were the highest since 1998.")]
    [InlineData("Move to the 2nd floor. Stay away from windows.")]
    [InlineData("Keep 1-2 torches, 20 candles and a radio ready.")]
    [InlineData("Turn off the gas. Do not use lifts. Call 999.")]
    public void Ordinary_guidance_prose_survives_untouched(string raw)
    {
        // False positives here delete safety instructions — the failure mode that actually hurts.
        Assert.Equal(raw, Clean(raw).Text);
    }

    [Fact]
    public void Three_or_more_newlines_collapse_to_a_blank_line()
    {
        var result = Clean("one\n\n\n\n\ntwo");

        Assert.Equal("one\n\ntwo", result.Text);
    }

    [Fact]
    public void Runs_of_spaces_collapse_to_one()
    {
        var result = Clean("stay     calm and    move");

        Assert.Equal("stay calm and move", result.Text);
    }

    [Fact]
    public void An_answer_exactly_at_the_cap_is_untouched()
    {
        var raw = new string('a', MaxLength);

        var result = Clean(raw);

        Assert.Equal(MaxLength, result.Text.Length);
        Assert.Equal(raw, result.Text);
    }

    [Fact]
    public void A_long_answer_is_clamped_at_a_sentence_boundary()
    {
        var sentence = new string('a', 99) + ". ";
        var raw = string.Concat(Enumerable.Repeat(sentence, 60)); // 6060 chars

        var result = Clean(raw);

        Assert.True(result.Text.Length <= MaxLength, $"expected <= {MaxLength}, was {result.Text.Length}");
        Assert.EndsWith(".", result.Text);
        Assert.False(result.Empty);
    }

    [Fact]
    public void A_long_answer_is_clamped_at_a_newline_boundary_when_one_is_closer()
    {
        var raw = string.Join("\n", Enumerable.Repeat(new string('b', 200), 30)); // 6029 chars

        var result = Clean(raw);

        Assert.True(result.Text.Length <= MaxLength, $"expected <= {MaxLength}, was {result.Text.Length}");
        Assert.EndsWith("b", result.Text);
        Assert.DoesNotContain("\n", result.Text[^1].ToString());
    }

    [Fact]
    public void A_long_answer_with_no_boundary_is_hard_cut_to_the_cap()
    {
        var result = Clean(new string('c', 4000));

        Assert.Equal(MaxLength, result.Text.Length);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t  ")]
    [InlineData("https://evil.example")]
    [InlineData("\u0000\u0001\u0002")]
    public void Answers_that_sanitise_to_nothing_report_empty(string? raw)
    {
        var result = Clean(raw);

        Assert.True(result.Empty);
        Assert.Equal(string.Empty, result.Text);
    }
}
