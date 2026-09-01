using FluentValidation;
using RapidRelief.Api.Features.Shelters.Domain;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Api.Features.Shelters.Endpoints;

public sealed record CreateShelterRequest(
    string Name,
    double Latitude,
    double Longitude,
    int Capacity,
    int CurrentOccupancy,
    List<string> Facilities,
    ShelterStatus Status);

public sealed class CreateShelterValidator : AbstractValidator<CreateShelterRequest>
{
    public CreateShelterValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180);
        RuleFor(x => x.Capacity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CurrentOccupancy)
            .GreaterThanOrEqualTo(0)
            .LessThanOrEqualTo(x => x.Capacity)
            .WithMessage("CurrentOccupancy cannot be greater than Capacity.");
    }
}

public sealed record UpdateShelterRequest(
    string Name,
    double Latitude,
    double Longitude,
    int Capacity,
    int CurrentOccupancy,
    List<string> Facilities,
    ShelterStatus Status);

public sealed class UpdateShelterValidator : AbstractValidator<UpdateShelterRequest>
{
    public UpdateShelterValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180);
        RuleFor(x => x.Capacity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.CurrentOccupancy)
            .GreaterThanOrEqualTo(0)
            .LessThanOrEqualTo(x => x.Capacity)
            .WithMessage("CurrentOccupancy cannot be greater than Capacity.");
    }
}

public sealed record UpdateOccupancyRequest(int CurrentOccupancy);

public sealed class UpdateOccupancyValidator : AbstractValidator<UpdateOccupancyRequest>
{
    public UpdateOccupancyValidator()
    {
        RuleFor(x => x.CurrentOccupancy).GreaterThanOrEqualTo(0);
    }
}

public sealed record ShelterDto(
    Guid Id,
    string Name,
    GeoPoint Location,
    int Capacity,
    int CurrentOccupancy,
    List<string> Facilities,
    ShelterStatus Status)
{
    public static ShelterDto FromEntity(Shelter shelter) =>
        new(shelter.Id, shelter.Name, shelter.Location, shelter.Capacity, shelter.CurrentOccupancy, shelter.Facilities, shelter.Status);

    public ShelterSummaryDto ToSummary() =>
        new(Id, Name, Location, Capacity, CurrentOccupancy, Status == ShelterStatus.Open);
}
