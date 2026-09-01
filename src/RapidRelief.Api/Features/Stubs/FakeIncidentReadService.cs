using RapidRelief.Api.Features.Stubs.SeedData;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Stubs;

/// <summary>Filters/pages <see cref="DhakaSeedData.Incidents"/> per IncidentQuery (blueprint B4).</summary>
public sealed class FakeIncidentReadService : IIncidentReadService
{
    public Task<PagedResult<IncidentSummaryDto>> GetIncidentsAsync(IncidentQuery query, CancellationToken ct = default)
    {
        IEnumerable<IncidentSummaryDto> filtered = DhakaSeedData.Incidents;
        if (query.Status is { } status)
        {
            filtered = filtered.Where(i => i.Status == status);
        }
        if (query.Type is { } type)
        {
            filtered = filtered.Where(i => i.Type == type);
        }
        if (query.MinSeverity is { } minSeverity)
        {
            filtered = filtered.Where(i => i.Severity >= minSeverity);
        }

        var ordered = filtered
            .OrderByDescending(i => i.ReportedAtUtc)
            .ThenBy(i => i.Id)
            .ToList();

        var page = Math.Clamp(query.Page, 1, 1_000_000);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);
        var items = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return Task.FromResult(new PagedResult<IncidentSummaryDto>(items, page, pageSize, ordered.Count));
    }

    public Task<IncidentSummaryDto?> GetByIdAsync(Guid incidentId, CancellationToken ct = default)
        => Task.FromResult(DhakaSeedData.Incidents.FirstOrDefault(i => i.Id == incidentId));
}
