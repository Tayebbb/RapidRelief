using FluentValidation;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Features.Relief.Endpoints;

public sealed record CreateReliefRequest(
    ResourceType Type,
    int Quantity,
    int RecipientCount,
    string? Urgency,
    double Latitude,
    double Longitude,
    string? DeliveryAddress,
    string? Notes,
    Guid? IncidentId,
    string? IdempotencyKey);

public sealed record UpdateReliefStatusRequest(ReliefStatus Status, string? Note);

public sealed record ReliefRequestDto(
    Guid Id,
    Guid RequesterId,
    ResourceType Type,
    int Quantity,
    int RecipientCount,
    string Urgency,
    ReliefStatus Status,
    GeoPoint Location,
    string DeliveryAddress,
    string Notes,
    Guid? IncidentId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed class CreateReliefValidator : AbstractValidator<CreateReliefRequest>
{
    public CreateReliefValidator()
    {
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.Quantity).InclusiveBetween(1, 1000);
        RuleFor(x => x.RecipientCount).InclusiveBetween(1, 500);
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180);
        RuleFor(x => x.Urgency).MaximumLength(30);
        RuleFor(x => x.DeliveryAddress).MaximumLength(250);
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.IdempotencyKey).MaximumLength(80);
    }
}

public sealed class UpdateReliefStatusValidator : AbstractValidator<UpdateReliefStatusRequest>
{
    public UpdateReliefStatusValidator()
    {
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Note).MaximumLength(500);
    }
}

public sealed record DispatchRequest(
    Guid ResourceId,
    double DispatchedQuantity,
    string CarrierOrPartner);

public sealed record ReliefDispatchDto(
    Guid Id,
    Guid ReliefRequestId,
    Guid ResourceId,
    string ResourceName,
    double DispatchedQuantity,
    string CarrierOrPartner,
    string Status,
    Guid DispatchedByUserId,
    DateTimeOffset DispatchedAtUtc,
    DateTimeOffset? DeliveredAtUtc);

public sealed class DispatchValidator : AbstractValidator<DispatchRequest>
{
    public DispatchValidator()
    {
        RuleFor(x => x.ResourceId).NotEmpty();
        RuleFor(x => x.DispatchedQuantity).GreaterThan(0).LessThanOrEqualTo(10_000_000);
        RuleFor(x => x.CarrierOrPartner).NotEmpty().MaximumLength(150);
    }
}

