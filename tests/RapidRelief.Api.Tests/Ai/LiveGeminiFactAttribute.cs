namespace RapidRelief.Api.Tests.Ai;

/// <summary>
/// Opt-in live-network fact: runs only when GEMINI_API_KEY is set in the environment;
/// otherwise the test reports as Skipped (xunit 2.9.3 ctor-set Skip).
/// </summary>
public sealed class LiveGeminiFactAttribute : FactAttribute
{
    public LiveGeminiFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY")))
        {
            Skip = "GEMINI_API_KEY not set — live Gemini smoke test skipped.";
        }
    }
}
