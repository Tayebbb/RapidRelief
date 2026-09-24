namespace RapidRelief.Api.Features.Ai.OpenRouter;

/// <summary>
/// Feature-local OpenRouter transport seam (NOT a contract, D-030/D-060). Takes the fully
/// built chat-completions request body (see <see cref="OpenRouterPromptBuilder"/> — the model
/// pins ride IN the body per D-061, the client reads no model config) and returns the raw
/// response body; parsing/validation is <see cref="OpenRouterResponseParser"/>'s job.
/// <paramref name="isVision"/> selects the D-026 timeout (10 s text / 20 s vision, config).
/// </summary>
internal interface IOpenRouterClient
{
    Task<string> SendAsync(string requestBody, bool isVision, CancellationToken ct = default);
}
