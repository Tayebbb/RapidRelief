using RapidRelief.Api.Features.Ai;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Api.Tests.Stubs;

public sealed class RuleBasedAiTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly GeoPoint Location = new(23.8103, 90.4125);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static RuleBasedAiAnalysisService CreateService() => new(new FixedTimeProvider(Now));

    private static AiAnalysisRequest Request(
        string description = "Street knee-deep in water",
        DisasterType type = DisasterType.Flood,
        bool isSos = false,
        DateTimeOffset? reportedAt = null)
        => new(Guid.Parse("a0000000-0000-0000-0000-000000000001"), type, description,
            Location, isSos, reportedAt ?? Now.AddHours(-1), Array.Empty<string>());

    [Fact]
    public async Task Analysis_is_deterministic_for_identical_input()
    {
        var service = CreateService();
        var request = Request();

        var first = await service.AnalyzeIncidentAsync(request);
        var second = await service.AnalyzeIncidentAsync(request);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Sos_request_outranks_identical_non_sos_request()
    {
        var service = CreateService();

        var sos = await service.AnalyzeIncidentAsync(Request(isSos: true));
        var normal = await service.AnalyzeIncidentAsync(Request(isSos: false));

        Assert.True(sos.PriorityScore > normal.PriorityScore,
            $"SOS priority {sos.PriorityScore} must exceed non-SOS {normal.PriorityScore}");
    }

    [Theory]
    [InlineData("people trapped under rubble, spreading fast", true, 0)]   // clamps down from >100
    [InlineData("minor waterlogging", false, -80)]                          // stale, low urgency
    [InlineData("children injured and trapped", true, -1)]
    public async Task Priority_is_always_within_0_and_100(string description, bool isSos, int reportedHoursOffset)
    {
        var service = CreateService();
        var request = Request(description: description, isSos: isSos, reportedAt: Now.AddHours(reportedHoursOffset));

        var result = await service.AnalyzeIncidentAsync(request);

        Assert.InRange(result.PriorityScore, 0, 100);
    }

    [Fact]
    public async Task Severity_bump_word_raises_severity_by_one()
    {
        var service = CreateService();

        var bumped = await service.AnalyzeIncidentAsync(Request(description: "family trapped on rooftop by water"));
        var calm = await service.AnalyzeIncidentAsync(Request(description: "street under water"));

        Assert.Equal(calm.EstimatedSeverity + 1, bumped.EstimatedSeverity);
    }

    [Fact]
    public async Task Severity_clamps_at_catastrophic()
    {
        var service = CreateService();
        // No type keywords in the description, so ReportedType (BuildingCollapse, highest base) is kept;
        // bump words drive the value into the clamp.
        var request = Request(description: "children trapped, many injured", type: DisasterType.BuildingCollapse);

        var result = await service.AnalyzeIncidentAsync(request);

        Assert.Equal(Severity.Catastrophic, result.EstimatedSeverity);
    }

    [Fact]
    public async Task Keyword_in_description_overrides_reported_type()
    {
        var service = CreateService();
        var request = Request(description: "warehouse on fire, smoke everywhere", type: DisasterType.Flood);

        var result = await service.AnalyzeIncidentAsync(request);

        Assert.Equal(DisasterType.Fire, result.PredictedType);
    }

    [Fact]
    public async Task Without_keywords_the_reported_type_is_kept()
    {
        var service = CreateService();
        var request = Request(description: "help needed urgently", type: DisasterType.Cyclone);

        var result = await service.AnalyzeIncidentAsync(request);

        Assert.Equal(DisasterType.Cyclone, result.PredictedType);
    }

    [Fact]
    public async Task Assessment_reports_rule_based_provider_and_no_duplicate()
    {
        var service = CreateService();

        var result = await service.AnalyzeIncidentAsync(Request());

        Assert.Equal("RuleBased", result.Provider);
        Assert.Null(result.PossibleDuplicateOfId);
        Assert.False(string.IsNullOrWhiteSpace(result.Summary));
        Assert.Equal(Request().IncidentId, result.IncidentId);
    }

    [Fact]
    public async Task Fresh_report_gets_recency_bonus_over_stale_identical_report()
    {
        var service = CreateService();

        var fresh = await service.AnalyzeIncidentAsync(Request(reportedAt: Now));
        var stale = await service.AnalyzeIncidentAsync(Request(reportedAt: Now.AddHours(-48)));

        Assert.True(fresh.PriorityScore > stale.PriorityScore);
    }
}
