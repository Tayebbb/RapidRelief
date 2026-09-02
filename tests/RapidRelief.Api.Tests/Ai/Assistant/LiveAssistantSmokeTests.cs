using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RapidRelief.Api.Features.Ai;
using RapidRelief.Api.Features.Ai.Assistant;
using RapidRelief.Api.Features.Ai.OpenRouter;

namespace RapidRelief.Api.Tests.Ai.Assistant;

/// <summary>
/// The ONE unverified wire detail in the migration is the assistant multi-turn shape against
/// the routed free models. A wrong shape is an HTTP 4xx, so this opt-in smoke sends a real
/// 2-turn history and asserts an OpenRouter answer came back: it must FAIL loudly rather
/// than degrade silently. Skipped without OPENROUTER_API_KEY.
/// </summary>
public sealed class LiveAssistantSmokeTests
{
    private sealed class LiveHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new()
        {
            BaseAddress = new Uri("https://openrouter.ai/"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    [LiveOpenRouterFact]
    public async Task Live_multi_turn_assistant_question_is_answered_by_openrouter()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ai:OpenRouter:ApiKey"] = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY"),
            ["Ai:OpenRouter:TextModel"] = Environment.GetEnvironmentVariable("OPENROUTER_TEXT_MODEL") ?? "z-ai/glm-5.2:free",
            ["Ai:OpenRouter:TextFallbackModel"] = "nvidia/nemotron-3-super-120b-a12b:free",
            ["Ai:OpenRouter:TimeoutSecondsText"] = "10",
        }).Build();
        var service = new OpenRouterAssistantService(
            new OpenRouterClient(new LiveHttpClientFactory(), config),
            new AiCircuitBreaker(TimeProvider.System, 3, TimeSpan.FromMinutes(2)),
            new AssistantOptions(), config, NullLogger<OpenRouterAssistantService>.Instance);

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
        Assert.Equal("OpenRouter", answer.Provider);
        Assert.False(string.IsNullOrWhiteSpace(answer.Text));
        Assert.True(answer.Text.Length <= new AssistantOptions().MaxAnswerLength);
        Assert.NotNull(answer.FinishReason);
    }
}
