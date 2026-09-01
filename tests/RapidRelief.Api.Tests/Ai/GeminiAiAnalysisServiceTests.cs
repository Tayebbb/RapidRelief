using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RapidRelief.Api.Features.Ai;
using RapidRelief.Api.Features.Ai.Gemini;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Api.Tests.Ai;

/// <summary>
/// F8 blueprint TEST PLAN item 2 — the D-028 provider chain: every failure mode yields
/// Provider=="RuleBased" and NEVER throws; a valid response yields Provider=="Gemini"
/// with telemetry populated; the breaker only counts genuine Gemini attempts.
/// </summary>
public sealed class GeminiAiAnalysisServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeGeminiClient : IGeminiClient
    {
        public int Calls;
        public Exception? Throws;
        public string? Response;

        public Task<string> GenerateContentAsync(AiAnalysisRequest request, CancellationToken ct = default)
        {
            Calls++;
            ct.ThrowIfCancellationRequested();
            if (Throws is not null)
            {
                throw Throws;
            }
            return Task.FromResult(Response!);
        }
    }

    private static AiAnalysisRequest Request(bool isSos = false)
        => new(Guid.NewGuid(), DisasterType.Flood, "Street knee-deep in water",
            new GeoPoint(23.8103, 90.4125), isSos, Now.AddHours(-1), Array.Empty<string>());

    private static string ValidBody(string predictedType = "Fire", int severity = 4,
        string summary = "Warehouse fire with heavy smoke.", string finishReason = "STOP")
    {
        var inner = $"{{\"predictedType\":\"{predictedType}\",\"severity\":{severity},\"summary\":{JsonSerializer.Serialize(summary)},\"confidence\":0.9}}";
        return $"{{\"candidates\":[{{\"content\":{{\"parts\":[{{\"text\":{JsonSerializer.Serialize(inner)}}}]}},\"finishReason\":{JsonSerializer.Serialize(finishReason)}}}],\"usageMetadata\":{{\"totalTokenCount\":57}}}}";
    }

    private static GeminiAiAnalysisService Create(
        FakeGeminiClient client, out GeminiCircuitBreaker breaker, string apiKey = "test-key")
    {
        var clock = new FixedTimeProvider(Now);
        breaker = new GeminiCircuitBreaker(clock, 3, TimeSpan.FromMinutes(2));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:Gemini:ApiKey"] = apiKey,
            ["Ai:Gemini:Model"] = "gemini-3.7-flash",
        }).Build();
        return new GeminiAiAnalysisService(new RuleBasedAiAnalysisService(clock), client, breaker,
            clock, config, NullLogger<GeminiAiAnalysisService>.Instance);
    }

    [Fact]
    public async Task Missing_api_key_short_circuits_to_rule_based_without_calling_the_client()
    {
        var client = new FakeGeminiClient { Response = ValidBody() };
        var service = Create(client, out var breaker, apiKey: "");

        var outcome = await service.AnalyzeWithMetadataAsync(Request());

        Assert.Equal("RuleBased", outcome.Assessment.Provider);
        Assert.Equal(0, client.Calls);
        Assert.Null(outcome.ModelName);
        Assert.Null(outcome.TokensUsed);
        Assert.True(breaker.TryEnter()); // short-circuit is NOT a breaker failure
    }

    [Fact]
    public async Task Repeated_missing_key_calls_never_open_the_breaker()
    {
        var client = new FakeGeminiClient { Response = ValidBody() };
        var service = Create(client, out var breaker, apiKey: "");

        for (var i = 0; i < 5; i++)
        {
            await service.AnalyzeWithMetadataAsync(Request());
        }

        Assert.True(breaker.TryEnter());
    }

    public static TheoryData<Exception> GeminiPathExceptions => new()
    {
        new GeminiUnavailableException("placeholder"),
        new HttpRequestException("429 too many requests"),
        new HttpRequestException("500 internal server error"),
        new TaskCanceledException("simulated request timeout"),
    };

    [Theory]
    [MemberData(nameof(GeminiPathExceptions))]
    public async Task Client_exceptions_fall_back_to_rule_based_and_never_throw(Exception exception)
    {
        var client = new FakeGeminiClient { Throws = exception };
        var service = Create(client, out _);

        var outcome = await service.AnalyzeWithMetadataAsync(Request());

        Assert.Equal("RuleBased", outcome.Assessment.Provider);
        Assert.Equal(1, client.Calls);
        Assert.InRange(outcome.Assessment.PriorityScore, 0, 100);
    }

    [Theory]
    [InlineData("this is not json")]
    [InlineData("{\"candidates\":[]}")]
    public async Task Malformed_response_body_falls_back_to_rule_based(string body)
    {
        var client = new FakeGeminiClient { Response = body };
        var service = Create(client, out _);

        var outcome = await service.AnalyzeWithMetadataAsync(Request());

        Assert.Equal("RuleBased", outcome.Assessment.Provider);
    }

    [Fact]
    public async Task Wrong_enum_value_falls_back_to_rule_based()
    {
        var client = new FakeGeminiClient { Response = ValidBody(predictedType: "Tsunami") };
        var service = Create(client, out _);

        var outcome = await service.AnalyzeWithMetadataAsync(Request());

        Assert.Equal("RuleBased", outcome.Assessment.Provider);
    }

    [Fact]
    public async Task Out_of_range_severity_falls_back_to_rule_based()
    {
        var client = new FakeGeminiClient { Response = ValidBody(severity: 7) };
        var service = Create(client, out _);

        var outcome = await service.AnalyzeWithMetadataAsync(Request());

        Assert.Equal("RuleBased", outcome.Assessment.Provider);
    }

    [Fact]
    public async Task Max_tokens_finish_reason_falls_back_to_rule_based()
    {
        var client = new FakeGeminiClient { Response = ValidBody(finishReason: "MAX_TOKENS") };
        var service = Create(client, out _);

        var outcome = await service.AnalyzeWithMetadataAsync(Request());

        Assert.Equal("RuleBased", outcome.Assessment.Provider);
    }

    [Fact]
    public async Task Valid_response_yields_gemini_provider_with_telemetry()
    {
        var client = new FakeGeminiClient { Response = ValidBody() };
        var service = Create(client, out _);
        var request = Request(isSos: true);

        var outcome = await service.AnalyzeWithMetadataAsync(request);

        Assert.Equal("Gemini", outcome.Assessment.Provider);
        Assert.Equal(DisasterType.Fire, outcome.Assessment.PredictedType);
        Assert.Equal(Severity.Severe, outcome.Assessment.EstimatedSeverity);
        Assert.Equal("Warehouse fire with heavy smoke.", outcome.Assessment.Summary);
        Assert.Equal(request.IncidentId, outcome.Assessment.IncidentId);
        Assert.Null(outcome.Assessment.PossibleDuplicateOfId);
        // Same shared formula as the rule-based path: 20*4 + 25 + 15*(1-1/6) = 117.5 → 100.
        Assert.Equal(PriorityFormula.Compute(Severity.Severe, true, request.ReportedAtUtc, Now),
            outcome.Assessment.PriorityScore);
        Assert.Equal("gemini-3.7-flash", outcome.ModelName);
        Assert.Equal(57, outcome.TokensUsed);
        Assert.Equal("STOP", outcome.FinishReason);
        Assert.True(outcome.LatencyMs >= 0);
    }

    [Fact]
    public async Task Three_failures_open_the_breaker_and_the_client_is_skipped()
    {
        var client = new FakeGeminiClient { Throws = new GeminiUnavailableException("down") };
        var service = Create(client, out _);

        for (var i = 0; i < 3; i++)
        {
            await service.AnalyzeWithMetadataAsync(Request());
        }
        Assert.Equal(3, client.Calls);

        var outcome = await service.AnalyzeWithMetadataAsync(Request());

        Assert.Equal("RuleBased", outcome.Assessment.Provider);
        Assert.Equal(3, client.Calls); // breaker open — no fourth attempt
    }

    [Fact]
    public async Task Success_between_failures_keeps_the_breaker_closed()
    {
        var client = new FakeGeminiClient { Throws = new GeminiUnavailableException("down") };
        var service = Create(client, out _);

        await service.AnalyzeWithMetadataAsync(Request());
        await service.AnalyzeWithMetadataAsync(Request());
        client.Throws = null;
        client.Response = ValidBody();
        await service.AnalyzeWithMetadataAsync(Request()); // success resets
        client.Throws = new GeminiUnavailableException("down again");
        await service.AnalyzeWithMetadataAsync(Request());
        await service.AnalyzeWithMetadataAsync(Request());

        var outcome = await service.AnalyzeWithMetadataAsync(Request());

        Assert.Equal(6, client.Calls); // never opened — every call reached the client
        Assert.Equal("RuleBased", outcome.Assessment.Provider);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_instead_of_falling_back()
    {
        var client = new FakeGeminiClient { Response = ValidBody() };
        var service = Create(client, out _);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.AnalyzeWithMetadataAsync(Request(), cts.Token));
    }
}
