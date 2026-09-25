using FluentValidation;
using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Api.Features.SafetyZones.Validation;

public sealed class CreateSafetyZoneRequestValidator : AbstractValidator<CreateSafetyZoneRequest>
{
    public CreateSafetyZoneRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.CenterLat).InclusiveBetween(-90, 90);
        RuleFor(x => x.CenterLng).InclusiveBetween(-180, 180);
        RuleFor(x => x.RadiusMeters).GreaterThanOrEqualTo(0);
        RuleFor(x => x.GeometryType)
            .NotEmpty()
            .Must(g => string.Equals(g, "Circle", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(g, "Polygon", StringComparison.OrdinalIgnoreCase))
            .WithMessage("GeometryType must be 'Circle' or 'Polygon'.");
        RuleFor(x => x.CoordinatesJson).MaximumLength(10000);
    }
}

public sealed class UpdateSafetyZoneRequestValidator : AbstractValidator<UpdateSafetyZoneRequest>
{
    public UpdateSafetyZoneRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.CenterLat).InclusiveBetween(-90, 90);
        RuleFor(x => x.CenterLng).InclusiveBetween(-180, 180);
        RuleFor(x => x.RadiusMeters).GreaterThanOrEqualTo(0);
        RuleFor(x => x.GeometryType)
            .NotEmpty()
            .Must(g => string.Equals(g, "Circle", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(g, "Polygon", StringComparison.OrdinalIgnoreCase))
            .WithMessage("GeometryType must be 'Circle' or 'Polygon'.");
        RuleFor(x => x.CoordinatesJson).MaximumLength(10000);
    }
}

public sealed class CreateRoadClosureRequestValidator : AbstractValidator<CreateRoadClosureRequest>
{
    public CreateRoadClosureRequestValidator()
    {
        RuleFor(x => x.RoadName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.BlockedReason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.AlternateRouteAdvice).MaximumLength(1000);
        RuleFor(x => x.CoordinatesJson).MaximumLength(10000);
    }
}

public sealed class UpdateRoadClosureRequestValidator : AbstractValidator<UpdateRoadClosureRequest>
{
    public UpdateRoadClosureRequestValidator()
    {
        RuleFor(x => x.RoadName).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.BlockedReason).NotEmpty().MaximumLength(500);
        RuleFor(x => x.AlternateRouteAdvice).MaximumLength(1000);
        RuleFor(x => x.CoordinatesJson).MaximumLength(10000);
    }
}
