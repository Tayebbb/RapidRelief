namespace RapidRelief.Api.Features.Ai.Assistant;

/// <summary>
/// Feature-local seam (NOT a contract, D-047): a pure OpenRouter-or-canned unit. The endpoint
/// owns history loading and context composition so this stays unit-testable with a fake
/// <c>IOpenRouterClient</c> alone.
/// </summary>
internal interface IAssistantService
{
    Task<AssistantAnswer> AskAsync(AssistantAsk ask, CancellationToken ct = default);
}

internal sealed record AssistantAsk(string Question, IReadOnlyList<AssistantTurn> History, AssistantContext Context);

internal sealed record AssistantTurn(bool FromUser, string Text);

/// <summary>
/// Facts the assistant is allowed to use. <paramref name="Operations"/> is role-scoped and built
/// server-side from the caller's own claims (D-102) — it never contains anything the caller could
/// not already read through the API.
/// </summary>
internal sealed record AssistantContext(
    bool HasLocation,
    IReadOnlyList<ShelterContext> Shelters,
    IReadOnlyList<string> Alerts,
    string Role = "",
    IReadOnlyList<string>? Operations = null)
{
    public static readonly AssistantContext None =
        new(HasLocation: false, Array.Empty<ShelterContext>(), Array.Empty<string>());

    public IReadOnlyList<string> OperationLines => Operations ?? Array.Empty<string>();
}

internal sealed record ShelterContext(string Name, double DistanceKm, int FreeCapacity);

internal sealed record AssistantAnswer(
    string Text,
    string Provider,
    bool Truncated,
    int LatencyMs,
    int? TokensUsed,
    string? FinishReason);
