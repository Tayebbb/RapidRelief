using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Features.Ai.Domain;

/// <summary>
/// F8-owned assessment row (table ai_assessments). Snapshot* columns copy the incident's
/// declared data from the IncidentCreated event (D-022) so duplicate detection needs no
/// cross-module query surface.
/// </summary>
public sealed class AiAssessment
{
    public Guid Id { get; set; }

    /// <summary>Unique — one assessment per incident (idempotency anchor).</summary>
    public Guid IncidentId { get; set; }

    public DisasterType PredictedType { get; set; }

    public Severity EstimatedSeverity { get; set; }

    public double PriorityScore { get; set; }

    public string Summary { get; set; } = string.Empty;

    public Guid? PossibleDuplicateOfId { get; set; }

    /// <summary>"OpenRouter" | "RuleBased".</summary>
    public string Provider { get; set; } = string.Empty;

    public string? ModelName { get; set; }

    /// <summary>Total analysis latency in ms, including any failed provider attempt before fallback.</summary>
    public int LatencyMs { get; set; }

    public int? TokensUsed { get; set; }

    public string? FinishReason { get; set; }

    public double SnapshotLatitude { get; set; }

    public double SnapshotLongitude { get; set; }

    public DisasterType SnapshotType { get; set; }

    public DateTimeOffset SnapshotReportedAtUtc { get; set; }

    public bool SnapshotIsSos { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
