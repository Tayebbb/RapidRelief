namespace RapidRelief.Api.Tests.Ai;

/// <summary>
/// Opt-in live-network fact: runs only when FREELLMPOOL_API_KEY OR FREELLMPOOL_BASE_URL is set
/// in the environment; otherwise the test reports as Skipped (xunit 2.9.3 ctor-set Skip).
/// freellmpool is keyless-capable (Pollinations, OVHcloud, Kilo Gateway, LLM7 need no
/// credentials), so gating on the API key alone would never let a keyless local sidecar run
/// this smoke — either var opting in is enough: an explicit key signals "hit a hosted/keyed
/// instance", an explicit base URL signals "hit my local docker-compose sidecar".
/// </summary>
public sealed class LiveFreeLlmPoolFactAttribute : FactAttribute
{
    public LiveFreeLlmPoolFactAttribute()
    {
        var hasKey = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FREELLMPOOL_API_KEY"));
        var hasBaseUrl = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FREELLMPOOL_BASE_URL"));
        if (!hasKey && !hasBaseUrl)
        {
            Skip = "Neither FREELLMPOOL_API_KEY nor FREELLMPOOL_BASE_URL is set — live FreeLlmPool smoke test skipped.";
        }
    }
}
