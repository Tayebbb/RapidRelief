using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Features.Incidents.Domain;

public sealed class IncidentReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReporterId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DisasterType DisasterType { get; set; }
    public Severity Severity { get; set; }
    public IncidentStatus Status { get; set; } = IncidentStatus.Reported;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string AddressOrArea { get; set; } = string.Empty;
    public int AffectedPeopleCount { get; set; }
    public bool IsSos { get; set; }
    public string ContactPhone { get; set; } = string.Empty;

    /// <summary>Client-supplied de-duplication key (retries, offline replay). Unique per reporter.</summary>
    public string? IdempotencyKey { get; set; }

    public int AiSeverityScore { get; set; }
    public string AiSummary { get; set; } = string.Empty;

    /// <summary>0-100 from F8; null until the assessment lands, so queues must sort with a fallback.</summary>
    public double? PriorityScore { get; set; }
    public Guid? PossibleDuplicateOfId { get; set; }

    public Guid? AssignedTeamId { get; set; }
    public Guid? AssignedMissionId { get; set; }
    public DateTimeOffset? AssignedAtUtc { get; set; }

    /// <summary>Last mission stage reported by F5 (Assigned/EnRoute/OnScene/Completed/Cancelled).</summary>
    public string? MissionStage { get; set; }

    public Guid? VerifiedByGovernmentId { get; set; }
    public DateTimeOffset? VerifiedAtUtc { get; set; }
    public string? RejectionReason { get; set; }
    public DateTimeOffset? ResolvedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<IncidentMedia> Media { get; set; } = new List<IncidentMedia>();
    public ICollection<IncidentStatusHistory> StatusHistory { get; set; } = new List<IncidentStatusHistory>();
}
