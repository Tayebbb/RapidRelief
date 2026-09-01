using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Shared.Contracts.Services;

public interface IUserAdminService
{
    Task<PagedResult<UserSummaryDto>> GetUsersAsync(int page = 1, int pageSize = 50, CancellationToken ct = default);
    Task<bool> SetLockedAsync(Guid userId, bool locked, CancellationToken ct = default);
    Task<bool> SetRolesAsync(Guid userId, IReadOnlyList<string> roles, CancellationToken ct = default);
}
