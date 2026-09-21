using Microsoft.AspNetCore.Identity;

namespace RapidRelief.Api.Features.Auth.Domain;

/// <summary>
/// Mapping between IdentityRole and AppPermission.
/// </summary>
public sealed class AppRolePermission
{
    public Guid RoleId { get; set; }
    public Guid PermissionId { get; set; }
    public DateTimeOffset AssignedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public IdentityRole<Guid>? Role { get; set; }
    public AppPermission? Permission { get; set; }
}
