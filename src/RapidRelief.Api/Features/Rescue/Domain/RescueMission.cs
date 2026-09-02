using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Features.Rescue.Domain;

public sealed class RescueMission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid IncidentId { get; set; }
    public Guid AssignedTeamId { get; set; }
    public string MissionTitle { get; set; } = string.Empty;
    public string Priority { get; set; } = "Urgent";
    public MissionStatus Status { get; set; } = MissionStatus.Assigned;
    public Guid AssignedByUserId { get; set; }
    public DateTimeOffset AssignedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public string OutcomeNotes { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public RescueTeam? Team { get; set; }
    public ICollection<RescueMissionLog> Logs { get; set; } = new List<RescueMissionLog>();
}
