using FluentValidation;
using RapidRelief.Shared.Contracts.Enums;
using AlertSeverity = RapidRelief.Shared.Contracts.Enums.Severity;

namespace RapidRelief.Api.Features.Alerts.Endpoints;

public sealed record CreateAlertRequest(
    string? Title,
    string? Body,
    AlertSeverity Severity,
    DisasterType? DisasterType,
    string? TargetArea,
    double? RadiusKm,
    DateTimeOffset ExpiresAtUtc);

public sealed class CreateAlertValidator : AbstractValidator<CreateAlertRequest>
{
    public CreateAlertValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(160);
        RuleFor(x => x.Body).NotEmpty().MaximumLength(500);
        RuleFor(x => x.TargetArea).NotEmpty().MaximumLength(150);
        RuleFor(x => x.RadiusKm).InclusiveBetween(0.1, 500).When(x => x.RadiusKm.HasValue);
        RuleFor(x => x.ExpiresAtUtc).Must(value => value > DateTimeOffset.UtcNow)
            .WithMessage("ExpiresAtUtc must be in the future.");
    }
}

public sealed record AlertDto(
    Guid Id,
    string Title,
    string Body,
    AlertSeverity Severity,
    DisasterType? DisasterType,
    string TargetArea,
    double? RadiusKm,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? RevokedAtUtc);
