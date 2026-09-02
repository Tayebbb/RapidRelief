namespace RapidRelief.Api.Features.Ai.Assistant;

/// <summary>
/// Feature-local seam (NOT a contract, D-047): a pure Gemini-or-canned unit. The endpoint
/// owns history loading and context composition so this stays unit-testable with a fake
/// <c>IGeminiClient</c> alone.
/// </summary>
internal interface IAssistantService
{
    Task<AssistantAnswer> AskAsync(AssistantAsk ask, CancellationToken ct = default);
}

internal sealed record AssistantAsk(string Question, IReadOnlyList<AssistantTurn> History, AssistantContext Context);

internal sealed record AssistantTurn(bool FromUser, string Text);

/// <summary>Alerts are always empty in v1 (D-052 — F10 has no read contract yet).</summary>
internal sealed record AssistantContext(
    bool HasLocation,
    IReadOnlyList<ShelterContext> Shelters,
    IReadOnlyList<string> Alerts)
{
    public static readonly AssistantContext None =
        new(HasLocation: false, Array.Empty<ShelterContext>(), Array.Empty<string>());
}

internal sealed record ShelterContext(string Name, double DistanceKm, int FreeCapacity);

internal sealed record AssistantAnswer(
    string Text,
    string Provider,
    bool Truncated,
    int LatencyMs,
    int? TokensUsed,
    string? FinishReason);
