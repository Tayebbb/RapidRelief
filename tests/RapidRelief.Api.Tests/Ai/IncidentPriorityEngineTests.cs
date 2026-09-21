using RapidRelief.Api.Features.Ai;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Api.Tests.Ai;

/// <summary>
/// The priority model must be explainable from real data: every point on the score traces to a
/// named factor with the evidence that earned it, and the wording is deterministic.
/// </summary>
public sealed class IncidentPriorityEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private static PriorityInputs Inputs(
        Severity severity = Severity.Moderate,
        bool isSos = false,
        int people = 0,
        bool medical = false,
        double hoursAgo = 0,
        double confidence = 1.0,
        int nearby = 0,
        ResponderAvailabilityDto? responders = null)
        => new(severity, isSos, people, medical, Now.AddHours(-hoursAgo), Now, confidence, nearby,
            responders ?? ResponderAvailabilityDto.Unknown);

    [Fact]
    public void Every_point_of_the_score_is_attributed_to_a_named_factor()
    {
        var result = IncidentPriorityEngine.Compute(Inputs(
            Severity.Severe, isSos: true, people: 12, medical: true, hoursAgo: 3, nearby: 2,
            responders: new ResponderAvailabilityDto(4, 0, 4, 6, null)));

        Assert.Equal(result.Score, Math.Round(Math.Min(100, result.Factors.Sum(f => f.Points)), 1));
        Assert.All(result.Factors, f =>
        {
            Assert.False(string.IsNullOrWhiteSpace(f.Label));
            Assert.False(string.IsNullOrWhiteSpace(f.Evidence));
        });

        var codes = result.Factors.Select(f => f.Code).ToList();
        Assert.Equal(["severity", "sos", "people", "medical", "waiting", "location", "resources"], codes);
    }

    [Fact]
    public void The_explanation_names_the_actual_evidence_not_generic_prose()
    {
        var result = IncidentPriorityEngine.Compute(Inputs(
            Severity.Severe, isSos: true, people: 12, medical: true, hoursAgo: 3));

        // The summary line carries the heaviest factors; the full list carries the rest.
        Assert.Contains("Critical", result.Explanation);
        Assert.Contains("emergency button", result.Explanation);
        Assert.Contains("medical emergency", result.Explanation);
        Assert.Contains("severe (4/5)", result.Explanation);

        Assert.Equal("12 reported affected", result.Factors.Single(f => f.Code == "people").Evidence);
        Assert.Contains("3 hours ago", result.Factors.Single(f => f.Code == "waiting").Evidence);
    }

    [Fact]
    public void An_sos_can_never_be_banded_below_high_however_thin_the_rest_of_the_evidence()
    {
        var result = IncidentPriorityEngine.Compute(Inputs(Severity.Minimal, isSos: true));

        Assert.True(result.Score < 60, $"expected a low raw score, got {result.Score}");
        Assert.Equal("High", result.Band);
        Assert.Equal(AiUrgency.Urgent, result.Urgency);
    }

    [Fact]
    public void Waiting_time_raises_priority_and_saturates_after_six_hours()
    {
        var fresh = IncidentPriorityEngine.Compute(Inputs(hoursAgo: 0));
        var waiting = IncidentPriorityEngine.Compute(Inputs(hoursAgo: 3));
        var stale = IncidentPriorityEngine.Compute(Inputs(hoursAgo: 6));
        var ancient = IncidentPriorityEngine.Compute(Inputs(hoursAgo: 48));

        Assert.True(waiting.Score > fresh.Score);
        Assert.True(stale.Score > waiting.Score);
        Assert.Equal(stale.Score, ancient.Score);
    }

    [Fact]
    public void Low_confidence_damps_the_severity_contribution_but_never_erases_it()
    {
        var certain = IncidentPriorityEngine.Compute(Inputs(Severity.Severe, confidence: 1.0));
        var unsure = IncidentPriorityEngine.Compute(Inputs(Severity.Severe, confidence: 0.0));

        var certainSeverity = certain.Factors.Single(f => f.Code == "severity").Points;
        var unsureSeverity = unsure.Factors.Single(f => f.Code == "severity").Points;

        Assert.True(unsureSeverity < certainSeverity);
        Assert.True(unsureSeverity >= certainSeverity * 0.7);
        Assert.Contains("confidence", unsure.Factors.Single(f => f.Code == "severity").Evidence);
    }

    [Fact]
    public void An_empty_team_registry_is_unknown_capacity_not_a_shortage()
    {
        var unknown = IncidentPriorityEngine.Compute(Inputs());
        var stretched = IncidentPriorityEngine.Compute(
            Inputs(responders: new ResponderAvailabilityDto(8, 1, 7, 9, null)));
        var comfortable = IncidentPriorityEngine.Compute(
            Inputs(responders: new ResponderAvailabilityDto(8, 6, 2, 2, null)));

        Assert.DoesNotContain(unknown.Factors, f => f.Code == "resources");
        Assert.DoesNotContain(comfortable.Factors, f => f.Code == "resources");
        Assert.Contains(stretched.Factors, f => f.Code == "resources");
        Assert.True(stretched.Score > comfortable.Score);
    }

    [Fact]
    public void No_team_free_is_stated_plainly_with_the_numbers_behind_it()
    {
        var result = IncidentPriorityEngine.Compute(
            Inputs(responders: new ResponderAvailabilityDto(5, 0, 5, 7, null)));

        var factor = result.Factors.Single(f => f.Code == "resources");
        Assert.Equal("No team free", factor.Label);
        Assert.Contains("All 5 teams are committed", factor.Evidence);
        Assert.Contains("7 open missions", factor.Evidence);
    }

    [Fact]
    public void People_affected_grows_sub_linearly_so_large_numbers_do_not_swamp_the_model()
    {
        var one = IncidentPriorityEngine.Compute(Inputs(people: 1)).Factors.Single(f => f.Code == "people").Points;
        var ten = IncidentPriorityEngine.Compute(Inputs(people: 10)).Factors.Single(f => f.Code == "people").Points;
        var thousand = IncidentPriorityEngine.Compute(Inputs(people: 1000)).Factors.Single(f => f.Code == "people").Points;

        Assert.True(ten > one);
        Assert.True(thousand > ten);
        Assert.True(thousand - ten < ten - one, "the 10→1000 step must matter less than the 1→10 step");
        Assert.True(thousand <= 10);
    }

    [Fact]
    public void The_score_is_deterministic_and_always_within_zero_and_one_hundred()
    {
        var inputs = Inputs(Severity.Catastrophic, isSos: true, people: 5000, medical: true,
            hoursAgo: 100, nearby: 50, responders: new ResponderAvailabilityDto(3, 0, 3, 12, 1.2));

        var first = IncidentPriorityEngine.Compute(inputs);
        var second = IncidentPriorityEngine.Compute(inputs);

        Assert.Equal(first.Score, second.Score);
        Assert.Equal(first.Explanation, second.Explanation);
        Assert.InRange(first.Score, 0, 100);
        Assert.Equal("Critical", first.Band);
        Assert.Equal(AiUrgency.Immediate, first.Urgency);
    }

    [Fact]
    public void A_quiet_report_with_nothing_aggravating_still_explains_itself()
    {
        var result = IncidentPriorityEngine.Compute(Inputs(Severity.Minimal, hoursAgo: 0));

        Assert.Equal("Low", result.Band);
        Assert.Equal(AiUrgency.Monitor, result.Urgency);
        Assert.Single(result.Factors);
        Assert.Contains("minimal (1/5)", result.Explanation);
    }
}
