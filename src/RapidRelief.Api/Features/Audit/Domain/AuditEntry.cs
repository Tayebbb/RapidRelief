namespace RapidRelief.Api.Features.Audit.Domain;

/// <summary>Immutable once written — the trail is append-only by design (F14).</summary>
public sealed class AuditEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ActorId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public string ActorRole { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;

    /// <summary>"Operator" for a direct admin action, "Event" for one projected off the bus.</summary>
    public string Source { get; set; } = AuditSource.Operator;

    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public static class AuditSource
{
    public const string Operator = "Operator";
    public const string Event = "Event";
}
