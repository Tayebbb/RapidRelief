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

    // ── Decision support (F8 intelligence pass) ──────────────────────────────────────────────

    /// <summary>0–1. The rule-based fallback reports its own fixed, deliberately modest value.</summary>
    public double Confidence { get; set; }

    /// <summary>Immediate | Urgent | Standard | Monitor.</summary>
    public string Urgency { get; set; } = string.Empty;

    /// <summary>Critical | High | Medium | Low — the band the priority score fell into.</summary>
    public string PriorityBand { get; set; } = string.Empty;

    public int? EstimatedPeopleAffected { get; set; }

    public bool MedicalUrgency { get; set; }

    /// <summary>JSON array of short observations; empty array when nothing was found.</summary>
    public string DamageIndicatorsJson { get; set; } = "[]";

    /// <summary>Why this classification and severity — evidence, not model prose about itself.</summary>
    public string Reasoning { get; set; } = string.Empty;

    /// <summary>JSON array of the scored priority factors that produced <see cref="PriorityScore"/>.</summary>
    public string PriorityFactorsJson { get; set; } = "[]";

    /// <summary>Set when the model path could not be used, naming the reason for the operator.</summary>
    public string? DegradedReason { get; set; }

    // ── Duplicate review (never auto-deleted; a human decides) ───────────────────────────────

    public double? DuplicateConfidence { get; set; }

    public string? DuplicateReason { get; set; }

    /// <summary>null = awaiting review, "Confirmed" or "Dismissed" once an operator has decided.</summary>
    public string? DuplicateDecision { get; set; }

    public Guid? DuplicateReviewedByUserId { get; set; }

    public DateTimeOffset? DuplicateReviewedAtUtc { get; set; }

    // ── Snapshots (D-022) ────────────────────────────────────────────────────────────────────

    public double SnapshotLatitude { get; set; }

    public double SnapshotLongitude { get; set; }

    public DisasterType SnapshotType { get; set; }

    public DateTimeOffset SnapshotReportedAtUtc { get; set; }

    public bool SnapshotIsSos { get; set; }

    /// <summary>Normalised, stop-worded word set of the description — the text-similarity fingerprint.</summary>
    public string SnapshotDescriptionKey { get; set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; set; }
}
