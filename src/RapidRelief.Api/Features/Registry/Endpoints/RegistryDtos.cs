using FluentValidation;

namespace RapidRelief.Api.Features.Registry.Endpoints;

public sealed record HospitalDto(
    Guid Id,
    string Name,
    double Latitude,
    double Longitude,
    int TotalBeds,
    int AvailableBeds,
    int IcuBeds,
    int AvailableIcuBeds,
    bool HasEmergency,
    IReadOnlyList<string> Specialties,
    string ContactNumber,
    string Address,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CreateHospitalRequest(
    string Name,
    double Latitude,
    double Longitude,
    int TotalBeds,
    int AvailableBeds,
    int IcuBeds,
    int AvailableIcuBeds,
    bool HasEmergency,
    List<string>? Specialties,
    string? ContactNumber,
    string? Address);

public sealed record UpdateHospitalRequest(
    string Name,
    double Latitude,
    double Longitude,
    int TotalBeds,
    int AvailableBeds,
    int IcuBeds,
    int AvailableIcuBeds,
    bool HasEmergency,
    List<string>? Specialties,
    string? ContactNumber,
    string? Address);

public sealed class CreateHospitalValidator : AbstractValidator<CreateHospitalRequest>
{
    public CreateHospitalValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180);
        RuleFor(x => x.TotalBeds).GreaterThanOrEqualTo(0);
        RuleFor(x => x.AvailableBeds).GreaterThanOrEqualTo(0)
            .LessThanOrEqualTo(x => x.TotalBeds).WithMessage("Available beds cannot exceed total beds.");
        RuleFor(x => x.IcuBeds).GreaterThanOrEqualTo(0);
        RuleFor(x => x.AvailableIcuBeds).GreaterThanOrEqualTo(0)
            .LessThanOrEqualTo(x => x.IcuBeds).WithMessage("Available ICU beds cannot exceed total ICU beds.");
    }
}

public sealed class UpdateHospitalValidator : AbstractValidator<UpdateHospitalRequest>
{
    public UpdateHospitalValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180);
        RuleFor(x => x.TotalBeds).GreaterThanOrEqualTo(0);
        RuleFor(x => x.AvailableBeds).GreaterThanOrEqualTo(0)
            .LessThanOrEqualTo(x => x.TotalBeds).WithMessage("Available beds cannot exceed total beds.");
        RuleFor(x => x.IcuBeds).GreaterThanOrEqualTo(0);
        RuleFor(x => x.AvailableIcuBeds).GreaterThanOrEqualTo(0)
            .LessThanOrEqualTo(x => x.IcuBeds).WithMessage("Available ICU beds cannot exceed total ICU beds.");
    }
}

public sealed record VolunteerDto(
    Guid Id,
    Guid? UserId,
    string FullName,
    double? Latitude,
    double? Longitude,
    IReadOnlyList<string> Skills,
    string Status,
    string ContactNumber,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CreateVolunteerRequest(
    string FullName,
    Guid? UserId,
    double? Latitude,
    double? Longitude,
    List<string>? Skills,
    string? Status,
    string? ContactNumber);

public sealed record UpdateVolunteerRequest(
    string FullName,
    double? Latitude,
    double? Longitude,
    List<string>? Skills,
    string? Status,
    string? ContactNumber);

public sealed class CreateVolunteerValidator : AbstractValidator<CreateVolunteerRequest>
{
    public CreateVolunteerValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        When(x => x.Latitude.HasValue, () => RuleFor(x => x.Latitude!.Value).InclusiveBetween(-90, 90));
        When(x => x.Longitude.HasValue, () => RuleFor(x => x.Longitude!.Value).InclusiveBetween(-180, 180));
    }
}

public sealed class UpdateVolunteerValidator : AbstractValidator<UpdateVolunteerRequest>
{
    public UpdateVolunteerValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(150);
        When(x => x.Latitude.HasValue, () => RuleFor(x => x.Latitude!.Value).InclusiveBetween(-90, 90));
        When(x => x.Longitude.HasValue, () => RuleFor(x => x.Longitude!.Value).InclusiveBetween(-180, 180));
    }
}

public sealed record NgoDto(
    Guid Id,
    string Name,
    double Latitude,
    double Longitude,
    IReadOnlyList<string> FocusAreas,
    string ContactPerson,
    string ContactEmail,
    string ContactNumber,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record CreateNgoRequest(
    string Name,
    double Latitude,
    double Longitude,
    List<string>? FocusAreas,
    string? ContactPerson,
    string? ContactEmail,
    string? ContactNumber);

public sealed record UpdateNgoRequest(
    string Name,
    double Latitude,
    double Longitude,
    List<string>? FocusAreas,
    string? ContactPerson,
    string? ContactEmail,
    string? ContactNumber);

public sealed class CreateNgoValidator : AbstractValidator<CreateNgoRequest>
{
    public CreateNgoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180);
    }
}

public sealed class UpdateNgoValidator : AbstractValidator<UpdateNgoRequest>
{
    public UpdateNgoValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180);
    }
}
