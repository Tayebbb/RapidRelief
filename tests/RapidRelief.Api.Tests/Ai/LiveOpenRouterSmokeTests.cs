using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RapidRelief.Api.Features.Ai;
using RapidRelief.Api.Features.Ai.OpenRouter;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Tests.Ai;

/// <summary>
/// ONE opt-in live smoke against the real OpenRouter API. Skipped unless OPENROUTER_API_KEY
/// is set (optional OPENROUTER_TEXT_MODEL overrides the D-061 pin). Text-only flood
/// classification: asserts the OpenRouter provider answered with a valid closed enum, an
/// in-range severity, the actually routed model, and finish_reason "stop" — completing
/// within the 10 s text timeout is the reasoning-disabled latency sanity check.
/// </summary>
public sealed class LiveOpenRouterSmokeTests
{
    private sealed class NullFileStorage : IFileStorage
    {
        public Task<StoredFile> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Stream?> OpenReadAsync(string path, CancellationToken ct = default)
            => Task.FromResult<Stream?>(null);

        public Task DeleteAsync(string path, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class LiveHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new()
        {
            BaseAddress = new Uri("https://openrouter.ai/"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    [LiveOpenRouterFact]
    public async Task Live_flood_description_is_classified_by_openrouter()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:OpenRouter:ApiKey"] = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY"),
            ["Ai:OpenRouter:TextModel"] = Environment.GetEnvironmentVariable("OPENROUTER_TEXT_MODEL") ?? "z-ai/glm-5.2:free",
            ["Ai:OpenRouter:TextFallbackModel"] = "nvidia/nemotron-3-super-120b-a12b:free",
            ["Ai:OpenRouter:TimeoutSecondsText"] = "10",
            ["Ai:OpenRouter:TimeoutSecondsVision"] = "20",
        }).Build();
        var client = new OpenRouterClient(new LiveHttpClientFactory(), config);
        var service = new OpenRouterAiAnalysisService(
            new RuleBasedAiAnalysisService(TimeProvider.System), client, new NullFileStorage(),
            new AiCircuitBreaker(TimeProvider.System, 3, TimeSpan.FromMinutes(2)),
            TimeProvider.System, config, NullLogger<OpenRouterAiAnalysisService>.Instance);

        var request = new AiAnalysisRequest(Guid.NewGuid(), DisasterType.Flood,
            "Monsoon flooding has submerged the ground floor of dozens of homes; water is "
            + "waist-deep and rising, and several elderly residents are trapped on upper floors.",
            new GeoPoint(23.8103, 90.4125), IsSos: true,
            DateTimeOffset.UtcNow.AddMinutes(-10), Array.Empty<string>());

        var outcome = await service.AnalyzeWithMetadataAsync(request);

        Assert.Equal("OpenRouter", outcome.Assessment.Provider);
        Assert.True(Enum.IsDefined(outcome.Assessment.PredictedType), "predictedType must be a valid DisasterType");
        Assert.InRange((int)outcome.Assessment.EstimatedSeverity, 1, 5);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Assessment.Summary));
        Assert.True(outcome.Assessment.Summary.Length <= 200);
        Assert.NotNull(outcome.ModelName); // the ACTUAL routed model (D-061)
        Assert.Equal("stop", outcome.FinishReason);
    }
}
