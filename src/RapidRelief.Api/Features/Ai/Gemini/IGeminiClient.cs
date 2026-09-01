using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Api.Features.Ai.Gemini;

/// <summary>
/// Feature-local Gemini transport seam (NOT a contract). Returns the raw generateContent
/// response body; parsing/validation is <see cref="GeminiResponseParser"/>'s job.
/// </summary>
internal interface IGeminiClient
{
    Task<string> GenerateContentAsync(AiAnalysisRequest request, CancellationToken ct = default);
}
