namespace RapidRelief.Shared.Contracts.ReadModels;

/// <summary>
/// One traceable administrative action: who did what, when, to which entity, and with what result.
/// Written through <c>IAuditTrail</c> so no feature has to know where the trail is stored.
/// </summary>
public sealed record AuditRecord(
    Guid? ActorId,
    string ActorName,
    string ActorRole,
    string Action,
    string EntityType,
    string EntityId,
    string Summary,
    string Result);

public sealed record AuditEntryDto(
    Guid Id,
    Guid? ActorId,
    string ActorName,
    string ActorRole,
    string Action,
    string EntityType,
    string EntityId,
    string Summary,
    string Result,
    string Source,
    DateTimeOffset OccurredAtUtc);
