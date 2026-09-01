namespace RapidRelief.Client.Features.Auth;

// Hand-written client mirrors of the server's slice-local wire DTOs (D-019). The canonical shapes
// live in src/RapidRelief.Api/Features/Auth/Endpoints/AuthDtos.cs and serialize camelCase both
// ways (System.Text.Json web defaults). Never reference the Api project — keep these in lockstep
// by hand.

public sealed record LoginRequest(string? Email, string? Password);

public sealed record RegisterRequest(string? Email, string? Password, string? DisplayName,
    string? PhoneNumber, string? EmergencyContact);

public sealed record UpdateProfileRequest(string? DisplayName, string? PhoneNumber, string? EmergencyContact);

public sealed record UserProfileDto(Guid Id, string Email, string DisplayName, string? PhoneNumber,
    string? EmergencyContact, bool HasPhoto, IReadOnlyList<string> Roles);

public sealed record AuthSessionDto(string AccessToken, DateTimeOffset ExpiresAtUtc, UserProfileDto User);
