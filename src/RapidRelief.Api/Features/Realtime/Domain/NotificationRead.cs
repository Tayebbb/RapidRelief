namespace RapidRelief.Api.Features.Realtime.Domain;

/// <summary>Per-user read marker (composite PK); cascades with its notification.</summary>
public sealed class NotificationRead
{
    public Guid NotificationId { get; set; }

    public Guid UserId { get; set; }

    public DateTimeOffset ReadAtUtc { get; set; }
}
