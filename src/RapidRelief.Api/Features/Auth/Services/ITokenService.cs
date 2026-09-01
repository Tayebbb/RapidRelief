using RapidRelief.Api.Features.Auth.Domain;

namespace RapidRelief.Api.Features.Auth.Services;

/// <summary>Slice-internal token minting/rotation contract (blueprint B4) — NOT a Shared contract.</summary>
public interface ITokenService
{
    (string AccessToken, DateTimeOffset ExpiresAtUtc) CreateAccessToken(AppUser user, IReadOnlyList<string> roles);

    /// <summary>Returns the RAW token (never persisted); pass null expiry for a new family (D-013).</summary>
    Task<(string RawToken, RefreshToken Row)> IssueRefreshTokenAsync(AppUser user, DateTimeOffset? inheritedAbsoluteExpiry, CancellationToken ct);

    Task<RefreshOutcome> ValidateAndRotateAsync(string rawToken, CancellationToken ct);

    /// <summary>Admin lock / role change / reuse detection (D-014).</summary>
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct);

    /// <summary>Logout — revokes that row only; no-op if unknown/already revoked.</summary>
    Task RevokeByRawTokenAsync(string rawToken, CancellationToken ct);
}

/// <summary>Every failure is the same shape — the HTTP response upstream is always a uniform 401.</summary>
public sealed record RefreshOutcome(
    bool Succeeded,
    string? AccessToken,
    DateTimeOffset? AccessExpiresAtUtc,
    string? NewRawRefreshToken,
    DateTimeOffset? RefreshExpiresAtUtc,
    AppUser? User,
    IReadOnlyList<string>? Roles);
