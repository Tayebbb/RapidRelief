namespace RapidRelief.Api.Features.Auth.Domain;

/// <summary>
/// Granular permission representing access to a specific page route or system capability.
/// NOT CURRENTLY ENFORCED (audit finding T5, 2026-09-24): seeded by <c>AuthSeeder</c> and
/// stored, but no endpoint, middleware, or service reads <see cref="AppRolePermission"/> to gate
/// access — every real authorization decision today goes through role-based
/// <c>[Authorize(Roles = ...)]</c>/<c>RequireAuthorization</c>, which the security audit verified
/// covers every mutating endpoint. Treat this table as seed data for a future fine-grained
/// permission layer, not as a live access control — do not assume a row here means a route is
/// actually gated.
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
