using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Shared.Contracts.Services;

public interface IIncidentReadService
{
    Task<PagedResult<IncidentSummaryDto>> GetIncidentsAsync(IncidentQuery query, CancellationToken ct = default);
    Task<IncidentSummaryDto?> GetByIdAsync(Guid incidentId, CancellationToken ct = default);
}
