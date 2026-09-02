using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Features.Incidents.Domain;

public sealed class IncidentStatusHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid IncidentId { get; set; }
    public IncidentStatus FromStatus { get; set; }
    public IncidentStatus ToStatus { get; set; }
    public Guid ChangedByUserId { get; set; }
    public string Notes { get; set; } = string.Empty;
    public DateTimeOffset ChangedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public IncidentReport? Incident { get; set; }
}
