namespace RapidRelief.Api.Features.Ai.Gemini;

/// <summary>
/// Feature-local Gemini transport seam (NOT a contract). Takes the fully built request body
/// (see <see cref="GeminiPromptBuilder"/>) and returns the raw generateContent response body;
/// parsing/validation is <see cref="GeminiResponseParser"/>'s job. <paramref name="isVision"/>
/// selects the D-026 timeout (10 s text / 20 s vision, config).
/// </summary>
internal interface IGeminiClient
{
    Task<string> GenerateContentAsync(string requestBody, bool isVision, CancellationToken ct = default);
}
