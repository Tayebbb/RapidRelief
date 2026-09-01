using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Api.Features.Ai.Gemini;

/// <summary>
/// Chunk-1 placeholder: always signals "Gemini unavailable" so the composite exercises the
/// rule-based fallback offline. Chunk 2 replaces the body with the real HTTP call (named
/// client "gemini", per-request key header, linked-CTS timeouts per D-026).
/// </summary>
internal sealed class GeminiClient : IGeminiClient
{
    public Task<string> GenerateContentAsync(AiAnalysisRequest request, CancellationToken ct = default)
        => throw new GeminiUnavailableException("Live Gemini transport arrives in F8 chunk 2.");
}
