using FluentValidation;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using Severity = RapidRelief.Shared.Contracts.Enums.Severity;

namespace RapidRelief.Api.Features.Incidents.Endpoints;

/// <summary>Wire shapes are feature-local (D-019) — Contracts stays untouched by F2.</summary>
public sealed record CreateIncidentRequest(
    string? Title,
    string? Description,
    DisasterType DisasterType,
    Severity Severity,
    double Latitude,
    double Longitude,
    string? AddressOrArea,
    int AffectedPeopleCount,
    bool IsSos,
    string? ContactPhone,
    IReadOnlyList<string>? PhotoPaths,
    string? IdempotencyKey);

public sealed record VerifyIncidentRequest(bool Approved, string? Reason);

public sealed record ResolveIncidentRequest(string? Notes);

public sealed record IncidentMediaDto(Guid Id, string Url, string MediaType, long SizeBytes, DateTimeOffset UploadedAtUtc);

public sealed record IncidentStatusEntryDto(IncidentStatus FromStatus, IncidentStatus ToStatus, string Notes, DateTimeOffset ChangedAtUtc);

public sealed record IncidentDto(
    Guid Id,
    Guid ReporterId,
    string Title,
    string Description,
    DisasterType DisasterType,
    Severity Severity,
    IncidentStatus Status,
    GeoPoint Location,
    string AddressOrArea,
    int AffectedPeopleCount,
    bool IsSos,
    /// <summary>Only populated for the reporter and for responders — never for other citizens.</summary>
    string? ContactPhone,
    double? PriorityScore,
    string AiSummary,
    Guid? PossibleDuplicateOfId,
    Guid? AssignedTeamId,
    Guid? AssignedMissionId,
    string? MissionStage,
    string? RejectionReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ResolvedAtUtc,
    IReadOnlyList<IncidentMediaDto> Media,
    IReadOnlyList<IncidentStatusEntryDto> Timeline);

public sealed record UploadedMediaDto(string Path, string Url, long SizeBytes, string ContentType);

public sealed class CreateIncidentValidator : AbstractValidator<CreateIncidentRequest>
{
    // D-011 carry-out: ingestion caps the description so a hostile report cannot blow the AI budget.
    public const int MaxDescriptionLength = 4000;

    public CreateIncidentValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(MaxDescriptionLength);
        RuleFor(x => x.DisasterType).IsInEnum();
        RuleFor(x => x.Severity).IsInEnum();
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180);
        RuleFor(x => x.AddressOrArea).MaximumLength(250);
        RuleFor(x => x.AffectedPeopleCount).InclusiveBetween(0, 1_000_000);
        RuleFor(x => x.ContactPhone).MaximumLength(30);
        RuleFor(x => x.IdempotencyKey).MaximumLength(80);
        RuleFor(x => x.PhotoPaths).Must(p => p is null || p.Count <= 5)
            .WithMessage("At most 5 photos may be attached to one report.");
    }
}

public sealed class VerifyIncidentValidator : AbstractValidator<VerifyIncidentRequest>
{
    public VerifyIncidentValidator()
    {
        RuleFor(x => x.Reason).MaximumLength(500);
        RuleFor(x => x.Reason).NotEmpty().When(x => !x.Approved)
            .WithMessage("A reason is required when rejecting a report.");
    }
}

public sealed class ResolveIncidentValidator : AbstractValidator<ResolveIncidentRequest>
{
    public ResolveIncidentValidator()
    {
        RuleFor(x => x.Notes).NotEmpty().MaximumLength(500)
            .WithMessage("Explain how the incident was resolved — the reporter is told.");
    }
}
