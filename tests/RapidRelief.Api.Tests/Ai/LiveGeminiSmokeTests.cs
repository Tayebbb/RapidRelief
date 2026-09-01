using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RapidRelief.Api.Features.Ai;
using RapidRelief.Api.Features.Ai.Gemini;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Tests.Ai;

/// <summary>
/// F8 blueprint TEST PLAN item 9 — ONE opt-in live smoke against the real Gemini API.
/// Skipped unless GEMINI_API_KEY is set (optional GEMINI_MODEL overrides the D-023 pin).
/// Text-only flood classification: asserts the Gemini provider answered with a valid closed
/// enum and an in-range severity — no exact-content assertions against a live model.
/// </summary>
public sealed class LiveGeminiSmokeTests
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
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    [LiveGeminiFact]
    public async Task Live_flood_description_is_classified_by_gemini()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:Gemini:ApiKey"] = Environment.GetEnvironmentVariable("GEMINI_API_KEY"),
            ["Ai:Gemini:Model"] = Environment.GetEnvironmentVariable("GEMINI_MODEL") ?? "gemini-3.7-flash",
            ["Ai:Gemini:TimeoutSecondsText"] = "10",
            ["Ai:Gemini:TimeoutSecondsVision"] = "20",
        }).Build();
        var client = new GeminiClient(new LiveHttpClientFactory(), config);
        var service = new GeminiAiAnalysisService(
            new RuleBasedAiAnalysisService(TimeProvider.System), client, new NullFileStorage(),
            new GeminiCircuitBreaker(TimeProvider.System, 3, TimeSpan.FromMinutes(2)),
            TimeProvider.System, config, NullLogger<GeminiAiAnalysisService>.Instance);

        var request = new AiAnalysisRequest(Guid.NewGuid(), DisasterType.Flood,
            "Monsoon flooding has submerged the ground floor of dozens of homes; water is "
            + "waist-deep and rising, and several elderly residents are trapped on upper floors.",
            new GeoPoint(23.8103, 90.4125), IsSos: true,
            DateTimeOffset.UtcNow.AddMinutes(-10), Array.Empty<string>());

        var outcome = await service.AnalyzeWithMetadataAsync(request);

        Assert.Equal("Gemini", outcome.Assessment.Provider);
        Assert.True(Enum.IsDefined(outcome.Assessment.PredictedType), "predictedType must be a valid DisasterType");
        Assert.InRange((int)outcome.Assessment.EstimatedSeverity, 1, 5);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Assessment.Summary));
        Assert.True(outcome.Assessment.Summary.Length <= 200);
        Assert.NotNull(outcome.ModelName);
        Assert.Equal("STOP", outcome.FinishReason);
    }
}
