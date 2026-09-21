using FluentValidation;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using Severity = RapidRelief.Shared.Contracts.Enums.Severity;

namespace RapidRelief.Api.Features.Rescue.Endpoints;

public sealed record CreateTeamRequest(string? TeamName, string? Specialization, string? ContactNumber, Guid? TeamLeadUserId);

public sealed record UpdateTeamRequest(string? TeamName, string? Specialization, string? ContactNumber, string? Status);

public sealed record AssignMissionRequest(Guid IncidentId, Guid? TeamId, string? MissionTitle, string? Priority);

public sealed record UpdateMissionStatusRequest(MissionStatus Status, string? Notes);

public sealed record RejectMissionRequest(string? Reason);

public sealed record ReassignMissionRequest(Guid TeamId, string? Reason);

public sealed record TeamPositionRequest(double Latitude, double Longitude, string? Status);

public sealed record TeamStatusRequest(string? Status);

public sealed record RescueTeamDto(
    Guid Id,
    string TeamName,
    string Specialization,
    string ContactNumber,
    string Status,
    Guid TeamLeadUserId,
    GeoPoint? CurrentLocation,
    int ActiveMissionCount);

public sealed record MissionLogDto(string StatusUpdate, string Message, DateTimeOffset TimestampUtc);

public sealed record RescueMissionDto(
    Guid Id,
    Guid IncidentId,
    Guid AssignedTeamId,
    string TeamName,
    string MissionTitle,
    string Priority,
    MissionStatus Status,
    DateTimeOffset AssignedAtUtc,
    DateTimeOffset? AcceptedAtUtc,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? OnSceneAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string OutcomeNotes,
    string? RejectionReason,
    IReadOnlyList<MissionLogDto> Logs);

public sealed record QueueItemDto(
    Guid IncidentId,
    DisasterType Type,
    Severity Severity,
    IncidentStatus Status,
    GeoPoint Location,
    string Summary,
    bool IsSos,
    double? PriorityScore,
    DateTimeOffset ReportedAtUtc,
    string Band,
    double? DistanceKm);

public sealed record TeamSuitabilityDto(
    Guid TeamId,
    string TeamName,
    string Specialization,
    string Status,
    double? DistanceKm,
    int ActiveMissions,
    IReadOnlyList<string> Reasons);

public sealed record RescueDashboardDto(
    IReadOnlyDictionary<string, int> QueueByBand,
    IReadOnlyList<QueueItemDto> Critical,
    IReadOnlyList<QueueItemDto> Nearby,
    int AssignedMissions,
    int ActiveMissions,
    int CompletedMissions,
    RescueTeamDto? MyTeam);

public sealed class CreateTeamValidator : AbstractValidator<CreateTeamRequest>
{
    public CreateTeamValidator()
    {
        RuleFor(x => x.TeamName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Specialization).MaximumLength(100);
        RuleFor(x => x.ContactNumber).MaximumLength(30);
    }
}

public sealed class UpdateTeamValidator : AbstractValidator<UpdateTeamRequest>
{
    public UpdateTeamValidator()
    {
        RuleFor(x => x.TeamName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Specialization).MaximumLength(100);
        RuleFor(x => x.ContactNumber).MaximumLength(30);
        RuleFor(x => x.Status).MaximumLength(30);
    }
}

public sealed class AssignMissionValidator : AbstractValidator<AssignMissionRequest>
{
    public AssignMissionValidator()
    {
        RuleFor(x => x.IncidentId).NotEmpty();
        RuleFor(x => x.MissionTitle).MaximumLength(200);
        RuleFor(x => x.Priority).MaximumLength(30);
    }
}

public sealed class UpdateMissionStatusValidator : AbstractValidator<UpdateMissionStatusRequest>
{
    public UpdateMissionStatusValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}

public sealed class TeamPositionValidator : AbstractValidator<TeamPositionRequest>
{
    public TeamPositionValidator()
    {
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180);
        RuleFor(x => x.Status).MaximumLength(30);
    }
}

public sealed class TeamStatusValidator : AbstractValidator<TeamStatusRequest>
{
    public TeamStatusValidator()
    {
        RuleFor(x => x.Status)
            .NotEmpty()
            .Must(s => s is not null && Domain.TeamStatus.IsKnown(s))
            .WithMessage("Status must be Available, Dispatched or OffDuty.");
    }
}

public sealed class RejectMissionValidator : AbstractValidator<RejectMissionRequest>
{
    public RejectMissionValidator()
    {
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500)
            .WithMessage("Say why the mission cannot be taken so it can be reassigned quickly.");
    }
}

public sealed class ReassignMissionValidator : AbstractValidator<ReassignMissionRequest>
{
    public ReassignMissionValidator()
    {
        RuleFor(x => x.TeamId).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(500);
    }
}
