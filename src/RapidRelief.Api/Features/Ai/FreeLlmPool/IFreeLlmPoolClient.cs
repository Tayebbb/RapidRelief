namespace RapidRelief.Api.Features.Ai.FreeLlmPool;

/// <summary>
/// Feature-local freellmpool transport seam (NOT a contract, D-030/D-113). Takes the fully
/// built OpenAI-compatible chat-completions request body (see <see cref="FreeLlmPoolPromptBuilder"/>
/// — the single model string rides in the body, the client reads no model config) and returns
/// the raw response body; parsing/validation is <see cref="FreeLlmPoolResponseParser"/>'s job.
/// <paramref name="isVision"/> selects the D-026 timeout (10 s text / 20 s vision, config).
/// </summary>
internal interface IFreeLlmPoolClient
{
    Task<string> SendAsync(string requestBody, bool isVision, CancellationToken ct = default);
}
