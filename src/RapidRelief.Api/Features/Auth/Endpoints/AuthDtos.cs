using FluentValidation;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Features.Auth.Endpoints;

// Slice-local wire DTOs (D-019) — NOT contracts; the client hand-mirrors these in chunk B.

public sealed record RegisterRequest(string? Email, string? Password, string? DisplayName,
    string? PhoneNumber, string? EmergencyContact, string? Role = null);

public sealed record GoogleSessionRequest(string? Email, string? DisplayName, string? ProviderUserId,
    string? PhotoUrl, string? Role = null);

public sealed record GoogleInitRequest(string? CallbackUrl = null);

public sealed record LoginRequest(string? Email, string? Password);

public sealed record UpdateProfileRequest(string? DisplayName, string? PhoneNumber, string? EmergencyContact);

public sealed record SetLockRequest(bool Locked);

public sealed record SetRolesRequest(IReadOnlyList<string>? Roles);

public sealed record UserProfileDto(Guid Id, string Email, string DisplayName, string? PhoneNumber,
    string? EmergencyContact, bool HasPhoto, IReadOnlyList<string> Roles);

public sealed record AuthSessionDto(string AccessToken, DateTimeOffset ExpiresAtUtc, UserProfileDto User);

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(128); // complexity is Identity's job
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PhoneNumber).MaximumLength(30);
        RuleFor(x => x.EmergencyContact).MaximumLength(100);
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        // NotEmpty only — no shape leaks on the login path.
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}

public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PhoneNumber).MaximumLength(30);
        RuleFor(x => x.EmergencyContact).MaximumLength(100);
    }
}

public sealed class SetRolesRequestValidator : AbstractValidator<SetRolesRequest>
{
    public SetRolesRequestValidator()
    {
        RuleFor(x => x.Roles).NotEmpty();
        RuleForEach(x => x.Roles)
            .Must(role => Roles.All.Contains(role, StringComparer.Ordinal)) // case-sensitive: "NGO" (risk 12)
            .WithMessage((_, role) => $"Unknown role '{role}'. Valid roles: {string.Join(", ", Roles.All)}.");
    }
}
