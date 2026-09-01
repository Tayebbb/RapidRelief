using RapidRelief.Api.Infrastructure.Auth;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Stubs;

/// <summary>
/// In-memory list of the 4 §5 seeded identities using FakeAuthHandler.SeedUserIds GUIDs;
/// SetLockedAsync/SetRolesAsync mutate in-memory and return false for unknown ids (blueprint B4).
/// </summary>
public sealed class FakeUserAdminService : IUserAdminService
{
    private readonly object _gate = new();
    private readonly List<UserSummaryDto> _users = Roles.All
        .Select(role => new UserSummaryDto(
            FakeAuthHandler.SeedUserIds[role],
            $"{role.ToLowerInvariant()}1@rr.dev",
            $"{role} One",
            [role],
            IsLocked: false))
        .ToList();

    public Task<PagedResult<UserSummaryDto>> GetUsersAsync(int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);
        lock (_gate)
        {
            var items = _users.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult(new PagedResult<UserSummaryDto>(items, page, pageSize, _users.Count));
        }
    }

    public Task<bool> SetLockedAsync(Guid userId, bool locked, CancellationToken ct = default)
        => Task.FromResult(Mutate(userId, user => user with { IsLocked = locked }));

    public Task<bool> SetRolesAsync(Guid userId, IReadOnlyList<string> roles, CancellationToken ct = default)
        => Task.FromResult(Mutate(userId, user => user with { Roles = roles }));

    private bool Mutate(Guid userId, Func<UserSummaryDto, UserSummaryDto> update)
    {
        lock (_gate)
        {
            var index = _users.FindIndex(u => u.Id == userId);
            if (index < 0)
            {
                return false;
            }
            _users[index] = update(_users[index]);
            return true;
        }
    }
}
