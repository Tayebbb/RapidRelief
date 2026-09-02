using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RapidRelief.Api.Features.Ai.Assistant;
using RapidRelief.Api.Features.Ai.Gemini;

namespace RapidRelief.Api.Tests.Ai.Assistant;

/// <summary>
/// F16 TEST PLAN item 15 — the ONE unverified wire detail in the blueprint is the assistant
/// turn role literal ("model") and the alternation rule. A wrong shape is an HTTP 400, so
/// this opt-in smoke sends a real 2-turn history and asserts a Gemini answer came back:
/// it must FAIL loudly rather than degrade silently. Skipped without GEMINI_API_KEY.
/// </summary>
public sealed class LiveAssistantSmokeTests
{
    private sealed class LiveHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new()
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    [LiveGeminiFact]
    public async Task Live_multi_turn_assistant_question_is_answered_by_gemini()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:Gemini:ApiKey"] = Environment.GetEnvironmentVariable("GEMINI_API_KEY"),
            ["Ai:Gemini:Model"] = Environment.GetEnvironmentVariable("GEMINI_MODEL") ?? "gemini-3.7-flash",
            ["Ai:Gemini:TimeoutSecondsText"] = "10",
        }).Build();
        var service = new GeminiAssistantService(
            new GeminiClient(new LiveHttpClientFactory(), config),
            new GeminiCircuitBreaker(TimeProvider.System, 3, TimeSpan.FromMinutes(2)),
            new AssistantOptions(), config, NullLogger<GeminiAssistantService>.Instance);

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

        // A wrong role literal or a broken alternation rule returns HTTP 400 ⇒ "Canned" here.
        Assert.Equal("Gemini", answer.Provider);
        Assert.False(string.IsNullOrWhiteSpace(answer.Text));
        Assert.True(answer.Text.Length <= new AssistantOptions().MaxAnswerLength);
        Assert.NotNull(answer.FinishReason);
    }
}
