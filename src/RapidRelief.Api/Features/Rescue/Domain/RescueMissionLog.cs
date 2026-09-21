namespace RapidRelief.Api.Features.Rescue.Domain;

public sealed class RescueMissionLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MissionId { get; set; }
    public Guid LoggedByUserId { get; set; }
    public string StatusUpdate { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;

    public RescueMission? Mission { get; set; }
}
