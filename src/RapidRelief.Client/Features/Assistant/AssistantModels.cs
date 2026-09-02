namespace RapidRelief.Client.Features.Assistant;

// Hand-mirrored F16 wire records (D-019 precedent). The Client never references the Api project;
// AssistantWireContractTests pins these against the server records property-for-property.

/// <summary>
/// The request carries ONLY a session id and a message (D-048): history is server-owned, so the
/// client can never supply a <c>role:"model"</c> turn.
/// </summary>
public sealed record AssistantMessageRequest(Guid? SessionId, string? Message, double? Latitude, double? Longitude);

public sealed record AssistantAnswerDto(string Text, string Provider, bool Truncated, DateTimeOffset CreatedAtUtc);

public sealed record AssistantMessageResponse(
    Guid? SessionId,
    AssistantAnswerDto Answer,
    bool Degraded,
    bool Persisted);

public sealed record AssistantMessageDto(Guid Id, string Role, string Text, string? Provider, DateTimeOffset CreatedAtUtc);

public sealed record AssistantHistoryResponse(Guid SessionId, IReadOnlyList<AssistantMessageDto> Messages);
