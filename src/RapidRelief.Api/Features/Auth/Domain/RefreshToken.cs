namespace RapidRelief.Api.Features.Auth.Domain;

/// <summary>
/// Server-side refresh token row (blueprint B2). Raw tokens are never persisted — only the
/// SHA-256 hex hash. Same-module FK by plain Guid, no navigation property (§4.3 habit).
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;             // SHA-256 of raw, uppercase hex, len 64 — UNIQUE
    public string SecurityStampAtIssue { get; set; } = string.Empty;  // max 100 (D-014 stamp check)
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }                  // absolute — inherited on rotation (D-013)
    public DateTimeOffset? RevokedAtUtc { get; set; }                 // null = active
    public string? ReplacedByTokenHash { get; set; }                  // len 64, audit chain
}
