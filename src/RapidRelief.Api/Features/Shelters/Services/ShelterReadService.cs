using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Shelters.Data;
using RapidRelief.Api.Features.Shelters.Domain;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Shelters.Services;

public sealed class ShelterReadService : IShelterReadService
{
    private readonly OpsDbContext _db;

    public ShelterReadService(OpsDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ShelterSummaryDto>> GetSheltersAsync(CancellationToken ct = default)
    {
        var shelters = await _db.Shelters
            .AsNoTracking()
            .ToListAsync(ct);

        return shelters.Select(s => new ShelterSummaryDto(
            s.Id,
            s.Name,
            s.Location,
            s.Capacity,
            s.CurrentOccupancy,
            s.Status == ShelterStatus.Open)).ToList();
    }

    public async Task<IReadOnlyList<ShelterSummaryDto>> GetNearestAsync(GeoPoint origin, int count = 5, CancellationToken ct = default)
    {
        // Client-side evaluation for Haversine is unavoidable because SQLite does not natively support geospatial Math functions (Math.Sin, Math.Cos).
        // Since the seeded and typical data sets for shelters are very small (<100), this is acceptable. 
        // In a real large-scale prod system, PostGIS extensions would be used via DbFunctions.
        var shelters = await _db.Shelters
            .AsNoTracking()
            .ToListAsync(ct);

        return shelters
            .OrderBy(s => HaversineHelper.CalculateMeters(origin, s.Location))
            .ThenBy(s => s.Id)
            .Take(Math.Max(count, 0))
            .Select(s => new ShelterSummaryDto(
                s.Id,
                s.Name,
                s.Location,
                s.Capacity,
                s.CurrentOccupancy,
                s.Status == ShelterStatus.Open))
            .ToList();
    }
}
