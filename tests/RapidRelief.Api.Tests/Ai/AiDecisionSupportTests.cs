using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RapidRelief.Api.Features.Ai;
using RapidRelief.Api.Features.Ai.Data;
using RapidRelief.Api.Features.Ai.OpenRouter;
using RapidRelief.Api.Features.Incidents.Data;
using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Tests.Ai;

/// <summary>
/// The decision-support contract end to end: structured output on a valid model answer, a usable
/// structured answer on every failure mode (invalid JSON, timeout, quota, outage, missing media),
/// and the explainability the operator sees. An AI outage must never stop an emergency report.
/// </summary>
public sealed class AiDecisionSupportTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly GeoPoint Location = new(23.8103, 90.4125);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ScriptedClient(Func<int, Task<string>> respond) : IOpenRouterClient
    {
        public int Calls { get; private set; }

        public Task<string> SendAsync(string requestBody, bool isVision, CancellationToken ct = default)
        {
            Calls++;
            LastBodyWasVision = isVision;
            return respond(Calls);
        }

        public bool LastBodyWasVision { get; private set; }
    }

    private sealed class NullFileStorage : IFileStorage
    {
        public Task<StoredFile> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Stream?> OpenReadAsync(string path, CancellationToken ct = default)
            => Task.FromResult<Stream?>(null);

        public Task DeleteAsync(string path, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static OpenRouterAiAnalysisService Service(IOpenRouterClient client, string apiKey = "sk-test")
    {
        var clock = new FixedTimeProvider(Now);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Ai:OpenRouter:ApiKey"] = apiKey })
            .Build();
        return new OpenRouterAiAnalysisService(
            new RuleBasedAiAnalysisService(clock), client, new NullFileStorage(),
            new AiCircuitBreaker(clock, 3, TimeSpan.FromMinutes(2)), clock, config,
            NullLogger<OpenRouterAiAnalysisService>.Instance);
    }

    private static AiAnalysisRequest Request(
        string description = "Water rising fast; two children trapped on the roof and one man injured.",
        DisasterType type = DisasterType.Flood,
        bool isSos = true,
        int people = 0,
        IReadOnlyList<string>? photos = null)
        => new(Guid.NewGuid(), type, description, Location, isSos, Now.AddMinutes(-5),
            photos ?? Array.Empty<string>(), Severity.Moderate, people);

    private static string ValidResponse(string inner) =>
        JsonSerializer.Serialize(new
        {
            model = "z-ai/glm-5.2:free",
            usage = new { total_tokens = 180 },
            choices = new[] { new { finish_reason = "stop", message = new { content = inner } } },
        });

    private const string RichInner = """
        {"predictedType":"Flood","severity":4,"summary":"Rapid flooding with people stranded on a roof.",
         "confidence":0.82,"damageIndicators":["Roof-level water","Two children stranded"],
         "estimatedPeopleAffected":6,"medicalUrgency":true,
         "reasoning":"The report describes water at roof level and children stranded, which is a severity 4 flood."}
        """;

    // ── Valid AI response ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_valid_model_answer_yields_the_full_structured_result()
    {
        var client = new ScriptedClient(_ => Task.FromResult(ValidResponse(RichInner)));
        var outcome = await Service(client).AnalyzeWithMetadataAsync(Request());

        Assert.Equal("OpenRouter", outcome.Assessment.Provider);
        Assert.Equal(DisasterType.Flood, outcome.Findings.PredictedType);
        Assert.Equal(Severity.Severe, outcome.Findings.EstimatedSeverity);
        Assert.Equal(0.82, outcome.Findings.Confidence, 3);
        Assert.Equal(6, outcome.Findings.EstimatedPeopleAffected);
        Assert.True(outcome.Findings.MedicalUrgency);
        Assert.Contains("Roof-level water", outcome.Findings.DamageIndicators);
        Assert.Contains("roof level", outcome.Findings.Reasoning, StringComparison.OrdinalIgnoreCase);
        Assert.Null(outcome.DegradedReason);
    }

    [Fact]
    public async Task Model_indicators_are_merged_with_the_evidence_read_from_the_text()
    {
        var client = new ScriptedClient(_ => Task.FromResult(ValidResponse(RichInner)));
        var outcome = await Service(client).AnalyzeWithMetadataAsync(Request());

        // The model's own observations survive AND the deterministic reader's do too.
        Assert.Contains(outcome.Findings.DamageIndicators, i => i.Contains("Roof-level water", StringComparison.Ordinal));
        Assert.Contains(outcome.Findings.DamageIndicators, i => i.StartsWith("People trapped", StringComparison.Ordinal));
        Assert.Contains(outcome.Findings.DamageIndicators, i => i.StartsWith("Injuries reported", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_reported_head_count_is_never_lowered_by_the_model()
    {
        var client = new ScriptedClient(_ => Task.FromResult(ValidResponse(RichInner)));
        var outcome = await Service(client).AnalyzeWithMetadataAsync(Request(people: 40));

        Assert.Equal(40, outcome.Findings.EstimatedPeopleAffected);
    }

    // ── Invalid AI response ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"predictedType":"Tsunami","severity":4,"summary":"x","confidence":0.5}""")]
    [InlineData("""{"predictedType":"Flood","severity":9,"summary":"x","confidence":0.5}""")]
    [InlineData("""{"predictedType":"Flood","summary":"x","confidence":0.5}""")]
    public async Task An_invalid_model_answer_falls_back_to_a_structured_rule_based_result(string inner)
    {
        var client = new ScriptedClient(_ => Task.FromResult(ValidResponse(inner)));
        var outcome = await Service(client).AnalyzeWithMetadataAsync(Request());

        Assert.Equal("RuleBased", outcome.Assessment.Provider);
        Assert.Equal(RuleBasedAiAnalysisService.RuleBasedConfidence, outcome.Findings.Confidence);
        Assert.NotEmpty(outcome.Findings.DamageIndicators);
        Assert.True(outcome.Findings.MedicalUrgency);
        Assert.Contains("no external model", outcome.Findings.Reasoning, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(outcome.DegradedReason);
    }

    [Fact]
    public async Task A_partial_model_answer_keeps_what_it_gave_and_fills_the_rest()
    {
        // Only the four originally-required fields — the decision-support extras are optional,
        // because rejecting this answer would trade a real assessment for none.
        const string terse = """{"predictedType":"Fire","severity":3,"summary":"Smoke reported.","confidence":0.6}""";
        var client = new ScriptedClient(_ => Task.FromResult(ValidResponse(terse)));

        var outcome = await Service(client).AnalyzeWithMetadataAsync(Request(description: "Smoke and injured people inside"));

        Assert.Equal("OpenRouter", outcome.Assessment.Provider);
        Assert.Equal(DisasterType.Fire, outcome.Findings.PredictedType);
        Assert.True(outcome.Findings.MedicalUrgency, "text evidence must still flag the medical urgency");
        Assert.NotEmpty(outcome.Findings.DamageIndicators);
        Assert.Contains("without stating its evidence", outcome.Findings.Reasoning);
    }

    // ── Timeout, quota, outage ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_provider_timeout_still_produces_an_assessment()
    {
        var client = new ScriptedClient(_ =>
            Task.FromException<string>(new AiProviderUnavailableException("OpenRouter text request timed out after 10 s", isTransient: true)));

        var outcome = await Service(client).AnalyzeWithMetadataAsync(Request());

        Assert.Equal("RuleBased", outcome.Assessment.Provider);
        Assert.Contains("unavailable", outcome.DegradedReason!, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(outcome.Assessment.PriorityScore, 0, 100);
    }

    [Fact]
    public async Task A_quota_failure_degrades_without_losing_the_report()
    {
        var client = new ScriptedClient(_ =>
            Task.FromException<string>(new AiProviderUnavailableException("OpenRouter returned HTTP 429", isTransient: true)));

        var outcome = await Service(client).AnalyzeWithMetadataAsync(Request());

        Assert.Equal("RuleBased", outcome.Assessment.Provider);
        Assert.NotEmpty(outcome.Findings.Summary);
    }

    [Fact]
    public async Task With_no_api_key_the_provider_is_never_called_and_the_reason_says_so()
    {
        var client = new ScriptedClient(_ => Task.FromResult(ValidResponse(RichInner)));

        var outcome = await Service(client, apiKey: string.Empty).AnalyzeWithMetadataAsync(Request());

        Assert.Equal(0, client.Calls);
        Assert.Equal("RuleBased", outcome.Assessment.Provider);
        Assert.Equal("No model provider is configured", outcome.DegradedReason);
    }

    [Fact]
    public async Task A_blocked_input_degrades_without_counting_against_the_breaker()
    {
        var client = new ScriptedClient(_ =>
            Task.FromException<string>(new AiProviderBlockedException("OpenRouter flagged the input (HTTP 403)")));
        var service = Service(client);

        var first = await service.AnalyzeWithMetadataAsync(Request());
        var second = await service.AnalyzeWithMetadataAsync(Request());
        var third = await service.AnalyzeWithMetadataAsync(Request());
        var fourth = await service.AnalyzeWithMetadataAsync(Request());

        Assert.All(new[] { first, second, third, fourth },
            o => Assert.Equal("RuleBased", o.Assessment.Provider));
        // Four attempts, four provider calls: a moderation verdict never opens the breaker.
        Assert.Equal(4, client.Calls);
        Assert.Equal("Model provider flagged the report text", fourth.DegradedReason);
    }

    // ── Missing media ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_photo_that_cannot_be_read_degrades_to_a_text_only_assessment()
    {
        var client = new ScriptedClient(_ => Task.FromResult(ValidResponse(RichInner)));

        var outcome = await Service(client).AnalyzeWithMetadataAsync(
            Request(photos: ["uploads/missing.jpg"]));

        Assert.Equal("OpenRouter", outcome.Assessment.Provider);
        Assert.False(client.LastBodyWasVision);
        Assert.Contains("No photo was available", outcome.Findings.Reasoning);
    }

    // ── Conflicting information ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_model_verdict_that_contradicts_the_reported_type_is_kept_and_explained()
    {
        const string contradicting = """
            {"predictedType":"Fire","severity":5,"summary":"Building well alight.","confidence":0.9,
             "damageIndicators":["Flames from two floors"],"estimatedPeopleAffected":3,"medicalUrgency":false,
             "reasoning":"The photo and text describe flames, not water, so this is a fire despite the flood tag."}
            """;
        var client = new ScriptedClient(_ => Task.FromResult(ValidResponse(contradicting)));

        var outcome = await Service(client).AnalyzeWithMetadataAsync(
            Request(description: "Fire on two floors, thick smoke", type: DisasterType.Flood));

        Assert.Equal(DisasterType.Fire, outcome.Findings.PredictedType);
        Assert.Contains("despite the flood tag", outcome.Findings.Reasoning);
    }

    [Fact]
    public async Task Text_evidence_of_a_medical_emergency_survives_a_model_that_denies_it()
    {
        const string denies = """
            {"predictedType":"Flood","severity":2,"summary":"Minor waterlogging.","confidence":0.7,
             "damageIndicators":[],"estimatedPeopleAffected":null,"medicalUrgency":false,
             "reasoning":"Looks like ordinary waterlogging."}
            """;
        var client = new ScriptedClient(_ => Task.FromResult(ValidResponse(denies)));

        var outcome = await Service(client).AnalyzeWithMetadataAsync(
            Request(description: "Man unconscious and bleeding in the flooded lane"));

        // Deterministic evidence is never overridden by a model's judgement call.
        Assert.True(outcome.Findings.MedicalUrgency);
        Assert.Contains(outcome.Findings.DamageIndicators, i => i.StartsWith("Injuries reported", StringComparison.Ordinal));
    }

    // ── Live pipeline ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_pipeline_persists_the_explanation_and_serves_it_as_decision_support()
    {
        await using var factory = new TestingWebAppFactory();
        var citizen = factory.CreateClient();
        citizen.DefaultRequestHeaders.Add(FakeAuthHandler.HeaderName, Roles.Citizen);

        var created = await citizen.PostAsJsonAsync("/api/incidents", new
        {
            title = "Roof collapse with people trapped",
            description = "Two children trapped under rubble after the roof caved in; one adult injured.",
            disasterType = DisasterType.BuildingCollapse,
            severity = Severity.Severe,
            latitude = 23.9001,
            longitude = 90.5001,
            addressOrArea = "Insight Lane",
            affectedPeopleCount = 5,
            isSos = true,
            idempotencyKey = "insight-1",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var incidentId = (await created.Content.ReadFromJsonAsync<ApiEnvelope<IncidentView>>())!.Data!.Id;

        var insight = await WaitForInsightAsync(factory, incidentId);

        Assert.Equal("RuleBased", insight.Provider);
        Assert.Equal(AiUrgency.Immediate, insight.Urgency);
        Assert.Equal("Critical", insight.PriorityBand);
        Assert.True(insight.MedicalUrgency);
        Assert.Equal(5, insight.EstimatedPeopleAffected);
        Assert.NotEmpty(insight.DamageIndicators);
        Assert.Contains(insight.PriorityFactors, f => f.Code == "sos");
        Assert.Contains(insight.PriorityFactors, f => f.Code == "people" && f.Evidence.Contains("5 reported", StringComparison.Ordinal));
        Assert.All(insight.PriorityFactors, f => Assert.False(string.IsNullOrWhiteSpace(f.Evidence)));
        Assert.Contains("Classified as BuildingCollapse", insight.Reasoning);
        Assert.True(insight.IsDecisionSupport);
    }

    [Fact]
    public async Task A_duplicate_is_flagged_for_review_and_neither_report_is_touched()
    {
        await using var factory = new TestingWebAppFactory();
        var citizen = factory.CreateClient();
        citizen.DefaultRequestHeaders.Add(FakeAuthHandler.HeaderName, Roles.Citizen);

        var firstId = await ReportAsync(citizen, "dup-a", 23.7501, 90.3501,
            "Fire in the market shed; smoke everywhere and stalls burning.");
        await WaitForInsightAsync(factory, firstId);

        var secondId = await ReportAsync(citizen, "dup-b", 23.7502, 90.3502,
            "Market shed on fire, smoke everywhere, stalls burning fast.");
        var second = await WaitForInsightAsync(factory, secondId);

        Assert.Equal(firstId, second.PossibleDuplicateOfId);
        Assert.NotNull(second.DuplicateConfidence);
        Assert.True(second.DuplicateConfidence >= 0.5);
        Assert.Contains("m apart", second.DuplicateReason!);
        Assert.Contains("share", second.DuplicateReason!);

        // Both reports remain live — flagging is advisory, never destructive.
        using var scope = factory.Services.CreateScope();
        var incidents = scope.ServiceProvider.GetRequiredService<IncidentsDbContext>();
        var rows = await incidents.Reports.AsNoTracking()
            .Where(x => x.Id == firstId || x.Id == secondId).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(IncidentStatus.Reported, r.Status));

        var government = factory.CreateClient();
        government.DefaultRequestHeaders.Add(FakeAuthHandler.HeaderName, Roles.Government);

        var pending = (await government.GetFromJsonAsync<ApiEnvelope<List<FlagView>>>("/api/ai/duplicates"))!.Data!;
        Assert.Contains(pending, f => f.IncidentId == secondId && f.Decision is null);

        var dismissed = await government.PostAsJsonAsync($"/api/ai/duplicates/{secondId}/dismiss",
            new { note = "Two separate stalls." });
        Assert.Equal(HttpStatusCode.OK, dismissed.StatusCode);

        var again = await government.PostAsJsonAsync($"/api/ai/duplicates/{secondId}/dismiss", new { note = "" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        // Still two live reports after the verdict.
        var after = await incidents.Reports.AsNoTracking()
            .CountAsync(x => x.Id == firstId || x.Id == secondId);
        Assert.Equal(2, after);
    }

    [Fact]
    public async Task Duplicate_review_is_government_only_and_insights_need_authentication()
    {
        await using var factory = new TestingWebAppFactory();

        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync($"/api/ai/insights/{Guid.NewGuid()}")).StatusCode);

        var citizen = factory.CreateClient();
        citizen.DefaultRequestHeaders.Add(FakeAuthHandler.HeaderName, Roles.Citizen);
        Assert.Equal(HttpStatusCode.Forbidden, (await citizen.GetAsync("/api/ai/duplicates")).StatusCode);

        var rescuer = factory.CreateClient();
        rescuer.DefaultRequestHeaders.Add(FakeAuthHandler.HeaderName, Roles.Rescuer);
        Assert.Equal(HttpStatusCode.OK, (await rescuer.GetAsync("/api/ai/duplicates")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await rescuer.PostAsJsonAsync($"/api/ai/duplicates/{Guid.NewGuid()}/confirm", new { note = "x" })).StatusCode);
    }

    private static async Task<Guid> ReportAsync(
        HttpClient citizen, string key, double lat, double lng, string description)
    {
        var response = await citizen.PostAsJsonAsync("/api/incidents", new
        {
            title = "Market fire",
            description,
            disasterType = DisasterType.Fire,
            severity = Severity.Moderate,
            latitude = lat,
            longitude = lng,
            addressOrArea = "Market Road",
            affectedPeopleCount = 2,
            isSos = false,
            idempotencyKey = key,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ApiEnvelope<IncidentView>>())!.Data!.Id;
    }

    /// <summary>The analyser runs on a background channel — poll rather than sleep a fixed time.</summary>
    private static async Task<InsightView> WaitForInsightAsync(TestingWebAppFactory factory, Guid incidentId)
    {
        var responder = factory.CreateClient();
        responder.DefaultRequestHeaders.Add(FakeAuthHandler.HeaderName, Roles.Government);

        for (var attempt = 0; attempt < 60; attempt++)
        {
            var response = await responder.GetAsync($"/api/ai/insights/{incidentId}");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return (await response.Content.ReadFromJsonAsync<ApiEnvelope<InsightView>>())!.Data!;
            }

            await Task.Delay(50);
        }

        using var scope = factory.Services.CreateScope();
        var assessed = await scope.ServiceProvider.GetRequiredService<AiDbContext>()
            .Assessments.AsNoTracking().CountAsync();
        Assert.Fail($"No assessment for {incidentId} after 3 s ({assessed} assessments exist).");
        return null!;
    }

    private sealed record IncidentView(Guid Id);

    private sealed record FactorView(string Code, string Label, double Points, string Evidence);

    private sealed record FlagView(Guid IncidentId, Guid PossibleDuplicateOfId, double Confidence,
        string Reason, string? Decision);

    private sealed record InsightView(
        Guid IncidentId,
        DisasterType PredictedType,
        Severity EstimatedSeverity,
        double Confidence,
        string Urgency,
        int? EstimatedPeopleAffected,
        bool MedicalUrgency,
        IReadOnlyList<string> DamageIndicators,
        string Summary,
        string Reasoning,
        double PriorityScore,
        string PriorityBand,
        IReadOnlyList<FactorView> PriorityFactors,
        string Provider,
        string? ModelName,
        Guid? PossibleDuplicateOfId,
        double? DuplicateConfidence,
        string? DuplicateReason,
        bool IsDecisionSupport);
}
