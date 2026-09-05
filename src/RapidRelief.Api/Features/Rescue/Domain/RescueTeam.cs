namespace RapidRelief.Api.Features.Rescue.Domain;

/// <summary>The three states a team can be in; "Dispatched" is system-driven by mission assignment.</summary>
public static class TeamStatus
{
    public const string Available = "Available";
    public const string Dispatched = "Dispatched";
    public const string OffDuty = "OffDuty";

    public static bool IsKnown(string value) =>
        value is Available or Dispatched or OffDuty;
}

public sealed class RescueTeam
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TeamName { get; set; } = string.Empty;
    public Guid TeamLeadUserId { get; set; }
    public string Specialization { get; set; } = "FloodRescue";
    public string ContactNumber { get; set; } = string.Empty;
    public string Status { get; set; } = "Available";
    public double? CurrentLatitude { get; set; }
    public double? CurrentLongitude { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<RescueTeamMember> Members { get; set; } = new List<RescueTeamMember>();
    public ICollection<RescueMission> Missions { get; set; } = new List<RescueMission>();
}
