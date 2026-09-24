using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Registry.Data;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Registry.Services;

public sealed class RegistryReadService(RegistryDbContext db) : IRegistryReadService
{
    public async Task<IReadOnlyList<HospitalSummaryDto>> GetHospitalsAsync(CancellationToken ct = default)
    {
        var hospitals = await db.Hospitals.AsNoTracking().OrderBy(h => h.Name).ToListAsync(ct);
        return hospitals.Select(h => new HospitalSummaryDto(
            h.Id,
            h.Name,
            h.Location,
            h.TotalBeds,
            h.AvailableBeds,
            h.Specialties.AsReadOnly()
        )).ToList();
    }

    public async Task<IReadOnlyList<VolunteerSummaryDto>> GetVolunteersAsync(CancellationToken ct = default)
    {
        var volunteers = await db.Volunteers.AsNoTracking().OrderBy(v => v.FullName).ToListAsync(ct);
        return volunteers.Select(v => new VolunteerSummaryDto(
            v.Id,
            v.FullName,
            v.Skills.AsReadOnly(),
            v.Status == "Available",
            v.Location
        )).ToList();
    }

    public async Task<IReadOnlyList<NgoSummaryDto>> GetNgosAsync(CancellationToken ct = default)
    {
        var ngos = await db.Ngos.AsNoTracking().OrderBy(n => n.Name).ToListAsync(ct);
        return ngos.Select(n => new NgoSummaryDto(
            n.Id,
            n.Name,
            n.FocusAreas.AsReadOnly(),
            n.ContactEmail
        )).ToList();
    }
}
