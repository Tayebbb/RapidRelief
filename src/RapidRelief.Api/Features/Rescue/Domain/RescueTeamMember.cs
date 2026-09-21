namespace RapidRelief.Api.Features.Rescue.Domain;

public sealed class RescueTeamMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TeamId { get; set; }
    public Guid RescuerUserId { get; set; }
    public DateTimeOffset JoinedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public RescueTeam? Team { get; set; }
}
