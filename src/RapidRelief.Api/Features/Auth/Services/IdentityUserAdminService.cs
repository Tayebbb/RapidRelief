using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Auth.Data;
using RapidRelief.Api.Features.Auth.Domain;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.Events;
using RapidRelief.Shared.Contracts.Eventing;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Auth.Services;

/// <summary>
/// Real IUserAdminService (blueprint B7) — adapts to the FROZEN contract exactly; displaces
/// FakeUserAdminService via stub-yield. IsLocked is computed in memory: LockoutEnd is not
/// ticks-gated and must never be compared inside SQL on SQLite (risk 8).
/// Self-lock/self-demote guards are deliberately NOT here: the frozen contract has no notion
/// of "caller", so UserAdminEndpoints owns that check — any new caller of this service must
/// enforce its own.
/// </summary>
public sealed class IdentityUserAdminService : IUserAdminService
{
    private const int MaxPage = 1_000_000;
    private const int MaxPageSize = 200;

    private readonly AuthDbContext _db;
    private readonly UserManager<AppUser> _userManager;
    private readonly ITokenService _tokenService;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;

    public IdentityUserAdminService(AuthDbContext db, UserManager<AppUser> userManager,
        ITokenService tokenService, IEventBus eventBus, TimeProvider timeProvider)
    {
        _db = db;
        _userManager = userManager;
        _tokenService = tokenService;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
    }

    public async Task<PagedResult<UserSummaryDto>> GetUsersAsync(int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        // Clamp BEFORE math (docs/api-conventions.md) — unclamped int.MaxValue overflows into a 500.
        page = Math.Clamp(page, 1, MaxPage);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var totalCount = await _db.Users.CountAsync(ct);
        var users = await _db.Users
            .OrderBy(u => u.Email)
            .ThenBy(u => u.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        // One grouped roles query for the page — no N+1.
        var userIds = users.Select(u => u.Id).ToList();
        var rolePairs = await _db.UserRoles
            .Where(ur => userIds.Contains(ur.UserId))
            .Join(_db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .ToListAsync(ct);
        var rolesByUser = rolePairs
            .GroupBy(p => p.UserId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(p => p.Name!).ToList());

        var now = _timeProvider.GetUtcNow();
        var items = users
            .Select(u => new UserSummaryDto(
                u.Id,
                u.Email ?? string.Empty,
                u.DisplayName,
                rolesByUser.TryGetValue(u.Id, out var roles) ? roles : [],
                IsLocked: u.LockoutEnabled && u.LockoutEnd.HasValue && u.LockoutEnd.Value > now))
            .ToList();

        return new PagedResult<UserSummaryDto>(items, page, pageSize, totalCount);
    }

    public async Task<bool> SetLockedAsync(Guid userId, bool locked, CancellationToken ct = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return false;
        }

        if (locked)
        {
            ThrowOnFailure(await _userManager.SetLockoutEnabledAsync(user, true));
            ThrowOnFailure(await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue));
            ThrowOnFailure(await _userManager.UpdateSecurityStampAsync(user));
            await _tokenService.RevokeAllForUserAsync(userId, ct); // D-014: effective immediately
        }
        else
        {
            ThrowOnFailure(await _userManager.SetLockoutEndDateAsync(user, null));
            ThrowOnFailure(await _userManager.ResetAccessFailedCountAsync(user));
        }

        await _eventBus.PublishAsync(new AuthEvent(userId, locked ? "Lock" : "Unlock", null), ct);
        return true;
    }

    public async Task<bool> SetRolesAsync(Guid userId, IReadOnlyList<string> roles, CancellationToken ct = default)
    {
        // Security backstop (risk 7): the endpoint validator already 400s unknown roles, so a
        // false here unambiguously means unknown user at the endpoint.
        if (roles.Any(role => !Roles.All.Contains(role, StringComparer.Ordinal)))
        {
            return false;
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return false;
        }

        var current = await _userManager.GetRolesAsync(user);
        var toRemove = current.Except(roles, StringComparer.Ordinal).ToList();
        var toAdd = roles.Except(current, StringComparer.Ordinal).ToList();
        if (toRemove.Count > 0)
        {
            ThrowOnFailure(await _userManager.RemoveFromRolesAsync(user, toRemove));
        }
        if (toAdd.Count > 0)
        {
            ThrowOnFailure(await _userManager.AddToRolesAsync(user, toAdd));
        }

        ThrowOnFailure(await _userManager.UpdateSecurityStampAsync(user));
        await _tokenService.RevokeAllForUserAsync(userId, ct); // D-014
        await _eventBus.PublishAsync(new AuthEvent(userId, "RoleChange", string.Join(",", roles)), ct);
        return true;
    }

    private static void ThrowOnFailure(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Identity admin operation failed: " + string.Join("; ", result.Errors.Select(e => $"{e.Code}: {e.Description}")));
        }
    }
}
