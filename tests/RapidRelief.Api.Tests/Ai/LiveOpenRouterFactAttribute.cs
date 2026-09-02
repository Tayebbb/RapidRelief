namespace RapidRelief.Api.Tests.Ai;

/// <summary>
/// Opt-in live-network fact: runs only when OPENROUTER_API_KEY is set in the environment;
/// otherwise the test reports as Skipped (xunit 2.9.3 ctor-set Skip).
/// </summary>
public sealed class LiveOpenRouterFactAttribute : FactAttribute
{
    public LiveOpenRouterFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENROUTER_API_KEY")))
        {
            Skip = "OPENROUTER_API_KEY not set — live OpenRouter smoke test skipped.";
        }
    }
}
