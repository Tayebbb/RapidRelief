using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RapidRelief.Api.Features.Ai;
using RapidRelief.Api.Features.Ai.Assistant;
using RapidRelief.Api.Features.Ai.FreeLlmPool;

namespace RapidRelief.Api.Tests.Ai.Assistant;

/// <summary>
/// The ONE unverified wire detail in the migration is the assistant multi-turn shape against
/// the routed free models. A wrong shape is an HTTP 4xx, so this opt-in smoke sends a real
/// 2-turn history and asserts a FreeLlmPool answer came back: it must FAIL loudly rather
/// than degrade silently. Skipped without FREELLMPOOL_API_KEY or FREELLMPOOL_BASE_URL.
/// </summary>
public sealed class LiveAssistantSmokeTests
{
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
    public async Task Live_multi_turn_assistant_question_is_answered_by_freellmpool()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:FreeLlmPool:BaseUrl"] = BaseUrl,
            ["Ai:FreeLlmPool:ApiKey"] = Environment.GetEnvironmentVariable("FREELLMPOOL_API_KEY"),
            ["Ai:FreeLlmPool:TextModel"] = Environment.GetEnvironmentVariable("FREELLMPOOL_TEXT_MODEL") ?? "quality",
            ["Ai:FreeLlmPool:TimeoutSecondsText"] = "10",
        }).Build();
        var service = new FreeLlmPoolAssistantService(
            new FreeLlmPoolClient(new LiveHttpClientFactory(), config),
            new AiCircuitBreaker(TimeProvider.System, 3, TimeSpan.FromMinutes(2)),
            new AssistantOptions(), config, NullLogger<FreeLlmPoolAssistantService>.Instance);

        var history = new[]
        {
            new AssistantTurn(true, "The water in my street is rising fast."),
            new AssistantTurn(false, "Move to higher ground now and take your phone with you."),
        };
        var context = new AssistantContext(
            HasLocation: true,
            new[] { new ShelterContext("Mirpur Girls School Shelter", 1.2, 40) },
            Array.Empty<string>());

        var answer = await service.AskAsync(new AssistantAsk("Which shelter should I go to?", history, context));

        // A wrong role literal or body shape returns HTTP 4xx ⇒ "Canned" here.
        Assert.Equal("FreeLlmPool", answer.Provider);
        Assert.False(string.IsNullOrWhiteSpace(answer.Text));
        Assert.True(answer.Text.Length <= new AssistantOptions().MaxAnswerLength);
        Assert.NotNull(answer.FinishReason);
    }
}
