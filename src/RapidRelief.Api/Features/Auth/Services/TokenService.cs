using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RapidRelief.Api.Features.Auth.Data;
using RapidRelief.Api.Features.Auth.Domain;
using RapidRelief.Shared.Contracts.Eventing;
using RapidRelief.Shared.Contracts.Events;

namespace RapidRelief.Api.Features.Auth.Services;

/// <summary>
/// Blueprint B4. Access JWTs are minted through the DEFAULT claim maps (never
/// MapInboundClaims=false) so round-tripped principals are identical to FakeAuth's (R9).
/// Refresh tokens: 32 random bytes, SHA-256 hex at rest, rotation inherits the absolute
/// expiry (D-013), reuse revokes the family (D-014).
/// </summary>
public sealed class TokenService : ITokenService
{
    private static readonly RefreshOutcome FailedOutcome = new(false, null, null, null, null, null, null);

    private readonly AuthDbContext _db;
    private readonly UserManager<AppUser> _userManager;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly IConfiguration _config;

    public TokenService(AuthDbContext db, UserManager<AppUser> userManager, IEventBus eventBus,
        TimeProvider timeProvider, IConfiguration config)
    {
        _db = db;
        _userManager = userManager;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _config = config;
    }

    public (string AccessToken, DateTimeOffset ExpiresAtUtc) CreateAccessToken(AppUser user, IReadOnlyList<string> roles)
    {
        var now = _timeProvider.GetUtcNow();
        var expires = now.AddMinutes(_config.GetValue("Jwt:AccessTokenMinutes", 30));

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Email ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateJwtSecurityToken(new SecurityTokenDescriptor
        {
            // Outbound map serializes these as nameid/unique_name/role; JwtBearer's inbound
            // map restores ClaimTypes.* — principal identical to FakeAuthHandler's (risk 6).
            Subject = new ClaimsIdentity(claims),
            Issuer = _config["Jwt:Issuer"],
            Audience = _config["Jwt:Audience"],
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:SigningKey"]!)),
                SecurityAlgorithms.HmacSha256),
        });
        return (handler.WriteToken(token), expires);
    }

    public async Task<(string RawToken, RefreshToken Row)> IssueRefreshTokenAsync(
        AppUser user, DateTimeOffset? inheritedAbsoluteExpiry, CancellationToken ct)
    {
        var (raw, row) = CreateRow(user, inheritedAbsoluteExpiry);
        _db.RefreshTokens.Add(row);
        await _db.SaveChangesAsync(ct);
        return (raw, row);
    }

    public async Task<RefreshOutcome> ValidateAndRotateAsync(string rawToken, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow();
        var hash = HashToken(rawToken);
        var row = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (row is null)
        {
            return FailedOutcome; // unknown token — uniform 401 upstream
        }

        if (row.RevokedAtUtc is not null)
        {
            // Reuse detected: the standard stolen-token response is family revocation (D-014).
            await RevokeAllForUserAsync(row.UserId, ct);
            await _eventBus.PublishAsync(new AuthEvent(row.UserId, "TokenReuse", null), ct);
            return FailedOutcome;
        }

        if (row.ExpiresAtUtc <= now)
        {
            return FailedOutcome;
        }

        var user = await _userManager.FindByIdAsync(row.UserId.ToString());
        if (user is null)
        {
            return FailedOutcome;
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            await RevokeAllForUserAsync(row.UserId, ct);
            return FailedOutcome;
        }

        if (!string.Equals(user.SecurityStamp, row.SecurityStampAtIssue, StringComparison.Ordinal))
        {
            await RevokeAllForUserAsync(row.UserId, ct);
            return FailedOutcome;
        }

        // Rotate: revoke old, insert heir inheriting the ABSOLUTE expiry (D-013), one save.
        row.RevokedAtUtc = now;
        var (newRaw, newRow) = CreateRow(user, row.ExpiresAtUtc);
        row.ReplacedByTokenHash = newRow.TokenHash;
        _db.RefreshTokens.Add(newRow);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Lost a concurrent rotation race on the same cookie (RevokedAtUtc concurrency token):
            // someone else already rotated or revoked this row — treat exactly like reuse (D-014).
            _db.ChangeTracker.Clear(); // drop the failed revoke+heir so RevokeAll saves cleanly
            await RevokeAllForUserAsync(row.UserId, ct);
            await _eventBus.PublishAsync(new AuthEvent(row.UserId, "TokenReuse", "ConcurrentRotation"), ct);
            return FailedOutcome;
        }

        var roles = (await _userManager.GetRolesAsync(user)).ToList(); // FRESH roles
        var (accessToken, accessExpires) = CreateAccessToken(user, roles);
        return new RefreshOutcome(true, accessToken, accessExpires, newRaw, newRow.ExpiresAtUtc, user, roles);
    }

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow();
        // Load-then-update, not ExecuteUpdate: provider-portable with the SQLite ticks converter (B4).
        var active = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAtUtc == null && t.ExpiresAtUtc > now)
            .ToListAsync(ct);
        foreach (var token in active)
        {
            token.RevokedAtUtc = now;
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task RevokeByRawTokenAsync(string rawToken, CancellationToken ct)
    {
        var hash = HashToken(rawToken);
        var row = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (row is { RevokedAtUtc: null })
        {
            row.RevokedAtUtc = _timeProvider.GetUtcNow();
            await _db.SaveChangesAsync(ct);
        }
    }

    private (string Raw, RefreshToken Row) CreateRow(AppUser user, DateTimeOffset? inheritedAbsoluteExpiry)
    {
        var now = _timeProvider.GetUtcNow();
        var raw = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var row = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = HashToken(raw),
            SecurityStampAtIssue = user.SecurityStamp!,
            CreatedAtUtc = now,
            ExpiresAtUtc = inheritedAbsoluteExpiry ?? now.AddDays(_config.GetValue("Jwt:RefreshTokenDays", 7)),
        };
        return (raw, row);
    }

    /// <summary>Raw is never persisted or logged — only this SHA-256 uppercase hex (len 64).</summary>
    private static string HashToken(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
