using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RapidRelief.Api.Features.Ai;
using RapidRelief.Api.Features.Ai.FreeLlmPool;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Tests.Ai;

/// <summary>
/// ONE opt-in live smoke against a real freellmpool instance. Skipped unless FREELLMPOOL_API_KEY
/// or FREELLMPOOL_BASE_URL is set (default http://localhost:8080/; optional FREELLMPOOL_TEXT_MODEL
/// overrides the "quality" routing alias). Text-only flood classification: asserts the
/// FreeLlmPool provider answered with a valid closed enum, an in-range severity, the actually
/// routed model, and finish_reason "stop" — completing within the 10 s text timeout is the
/// latency sanity check.
/// </summary>
public sealed class LiveFreeLlmPoolSmokeTests
{
    private sealed class NullFileStorage : IFileStorage
    {
        public Task<StoredFile> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Stream?> OpenReadAsync(string path, CancellationToken ct = default)
            => Task.FromResult<Stream?>(null);

        public Task DeleteAsync(string path, CancellationToken ct = default) => Task.CompletedTask;
    }

    private static string BaseUrl
        => Environment.GetEnvironmentVariable("FREELLMPOOL_BASE_URL") is { Length: > 0 } url
            ? url
            : "http://localhost:8080/";

    private sealed class LiveHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new()
        {
            BaseAddress = new Uri(BaseUrl),
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    [LiveFreeLlmPoolFact]
    public async Task Live_flood_description_is_classified_by_freellmpool()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:FreeLlmPool:BaseUrl"] = BaseUrl,
            ["Ai:FreeLlmPool:ApiKey"] = Environment.GetEnvironmentVariable("FREELLMPOOL_API_KEY"),
            ["Ai:FreeLlmPool:TextModel"] = Environment.GetEnvironmentVariable("FREELLMPOOL_TEXT_MODEL") ?? "quality",
            ["Ai:FreeLlmPool:TimeoutSecondsText"] = "10",
            ["Ai:FreeLlmPool:TimeoutSecondsVision"] = "20",
        }).Build();
        var client = new FreeLlmPoolClient(new LiveHttpClientFactory(), config);
        var service = new FreeLlmPoolAiAnalysisService(
            new RuleBasedAiAnalysisService(TimeProvider.System), client, new NullFileStorage(),
            new AiCircuitBreaker(TimeProvider.System, 3, TimeSpan.FromMinutes(2)),
            TimeProvider.System, config, NullLogger<FreeLlmPoolAiAnalysisService>.Instance);

        var request = new AiAnalysisRequest(Guid.NewGuid(), DisasterType.Flood,
            "Monsoon flooding has submerged the ground floor of dozens of homes; water is "
            + "waist-deep and rising, and several elderly residents are trapped on upper floors.",
            new GeoPoint(23.8103, 90.4125), IsSos: true,
            DateTimeOffset.UtcNow.AddMinutes(-10), Array.Empty<string>());

        var outcome = await service.AnalyzeWithMetadataAsync(request);

        Assert.Equal("FreeLlmPool", outcome.Assessment.Provider);
        Assert.True(Enum.IsDefined(outcome.Assessment.PredictedType), "predictedType must be a valid DisasterType");
        Assert.InRange((int)outcome.Assessment.EstimatedSeverity, 1, 5);
        Assert.False(string.IsNullOrWhiteSpace(outcome.Assessment.Summary));
        Assert.True(outcome.Assessment.Summary.Length <= 200);
        Assert.NotNull(outcome.ModelName); // the ACTUAL routed model
        Assert.Equal("stop", outcome.FinishReason);
    }
}
