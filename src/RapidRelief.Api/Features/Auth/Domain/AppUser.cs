using Microsoft.AspNetCore.Identity;

namespace RapidRelief.Api.Features.Auth.Domain;

/// <summary>F1 identity user (blueprint B2); PhoneNumber/Email/SecurityStamp/LockoutEnd inherited.</summary>
public sealed class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;   // required, max 100
    public string? EmergencyContact { get; set; }             // max 100
    public string? PhotoPath { get; set; }                    // max 260 — relative IFileStorage path
}
