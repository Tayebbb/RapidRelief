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
    public int AiSeverityScore { get; set; }
    public string AiSummary { get; set; } = string.Empty;
    public Guid? VerifiedByGovernmentId { get; set; }
    public DateTimeOffset? VerifiedAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<IncidentMedia> Media { get; set; } = new List<IncidentMedia>();
    public ICollection<IncidentStatusHistory> StatusHistory { get; set; } = new List<IncidentStatusHistory>();
}
