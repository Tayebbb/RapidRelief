namespace RapidRelief.Api.Features.Ai.Domain;

/// <summary>Who produced the turn (D-048); persisted as int.</summary>
public enum AssistantRole
{
    User = 0,
    Model = 1,
}

/// <summary>
/// Server-owned conversation turn (table ai_assistant_messages, D-048). The client never
/// supplies history — a forged SessionId can only ever read rows owned by the caller.
/// </summary>
public sealed class AssistantMessage
{
    public Guid Id { get; set; }

    public Guid SessionId { get; set; }

    /// <summary>Ownership filter for every read, write and delete.</summary>
    public Guid UserId { get; set; }

    public AssistantRole Role { get; set; }

    public string Text { get; set; } = string.Empty;

    /// <summary>"Gemini" | "Canned"; null on user rows.</summary>
    public string? Provider { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
