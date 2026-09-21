namespace RapidRelief.Api.Features.Auth.Domain;

/// <summary>
/// Granular permission representing access to a specific page route or system capability.
/// </summary>
public sealed class AppPermission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string PageRoute { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<AppRolePermission> RolePermissions { get; set; } = new List<AppRolePermission>();
}
