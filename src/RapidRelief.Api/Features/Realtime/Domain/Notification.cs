namespace RapidRelief.Api.Features.Realtime.Domain;

/// <summary>Fan-out target of a notification row. Stored as a string (8 chars max).</summary>
public static class NotificationAudience
{
    public const string All = "All";
    public const string Role = "Role";
    public const string User = "User";
}

/// <summary>
/// One persisted notification (D-042 table <c>notifications_notification</c>). UserId is a
/// bare Guid — no cross-module FK or navigation property (§4.3).
/// </summary>
public sealed class Notification
{
    public const int MaxPayloadChars = 4000;
    public const int MaxSummaryChars = 160;
    public const int MaxTopicChars = 64;

    public Guid Id { get; set; }

    public string Audience { get; set; } = NotificationAudience.All;

    public string? Role { get; set; }

    public Guid? UserId { get; set; }

    public string Topic { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }
}
