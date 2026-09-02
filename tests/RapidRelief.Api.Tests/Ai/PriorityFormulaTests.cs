using RapidRelief.Api.Features.Ai;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Api.Tests.Ai;

/// <summary>
/// F8 blueprint TEST PLAN item 6 — the extracted formula is pinned exactly and proven
/// byte-identical to what RuleBasedAiAnalysisService produces (refactor = zero behavior change).
/// </summary>
public sealed class PriorityFormulaTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public void Catastrophic_sos_fresh_clamps_to_100()
    {
        // 20*5 + 25 + 15 = 140 → clamp 100
        Assert.Equal(100, PriorityFormula.Compute(Severity.Catastrophic, isSos: true, Now, Now));
    }

    [Fact]
    public void Minimal_stale_scores_exactly_20()
    {
        // 20*1 + 0 + 0 (age ≥ 6 h kills the recency bonus)
        Assert.Equal(20, PriorityFormula.Compute(Severity.Minimal, isSos: false, Now.AddHours(-80), Now));
    }

    [Fact]
    public void Moderate_three_hours_old_gets_half_recency_bonus()
    {
        // 20*3 + 0 + 15*(1 - 3/6) = 67.5
        Assert.Equal(67.5, PriorityFormula.Compute(Severity.Moderate, isSos: false, Now.AddHours(-3), Now));
    }

    [Fact]
    public void Future_reported_time_clamps_age_to_zero_and_grants_full_bonus()
    {
        // age negative → 0 → full 15 bonus: 20*2 + 0 + 15 = 55
        Assert.Equal(55, PriorityFormula.Compute(Severity.Minor, isSos: false, Now.AddHours(2), Now));
    }

    [Fact]
    public void Sos_adds_exactly_25()
    {
        var without = PriorityFormula.Compute(Severity.Minor, isSos: false, Now.AddHours(-10), Now);
        var with = PriorityFormula.Compute(Severity.Minor, isSos: true, Now.AddHours(-10), Now);

        Assert.Equal(25, with - without);
    }

    [Theory]
    [InlineData("Street knee-deep in water", DisasterType.Flood, false, -1.0)]
    [InlineData("people trapped under rubble", DisasterType.BuildingCollapse, true, 0.0)]
    [InlineData("minor waterlogging in the lane", DisasterType.Other, false, -12.0)]
    [InlineData("fire spreading with children injured", DisasterType.Fire, true, -0.25)]
    public async Task RuleBased_service_score_equals_the_shared_formula(
        string description, DisasterType reportedType, bool isSos, double reportedHoursOffset)
    {
        var service = new RuleBasedAiAnalysisService(new FixedTimeProvider(Now));
        var reportedAt = Now.AddHours(reportedHoursOffset);
        var request = new AiAnalysisRequest(Guid.NewGuid(), reportedType, description,
            new GeoPoint(23.8103, 90.4125), isSos, reportedAt, Array.Empty<string>());

        var result = await service.AnalyzeIncidentAsync(request);

        Assert.Equal(
            PriorityFormula.Compute(result.EstimatedSeverity, isSos, reportedAt, Now),
            result.PriorityScore);
    }

    [Fact]
    public async Task RuleBased_golden_output_is_unchanged_by_the_refactor()
    {
        // Pre-refactor golden: Flood default (no keyword/bump words), age 1 h
        // → severity Moderate(3), priority 20*3 + 15*(1 - 1/6) = 72.5.
        var service = new RuleBasedAiAnalysisService(new FixedTimeProvider(Now));
        var request = new AiAnalysisRequest(Guid.NewGuid(), DisasterType.Flood, "Street knee-deep in water",
            new GeoPoint(23.8103, 90.4125), false, Now.AddHours(-1), Array.Empty<string>());

        var result = await service.AnalyzeIncidentAsync(request);

        Assert.Equal(Severity.Moderate, result.EstimatedSeverity);
        Assert.Equal(72.5, result.PriorityScore);
        Assert.Equal("RuleBased", result.Provider);
    }
}
