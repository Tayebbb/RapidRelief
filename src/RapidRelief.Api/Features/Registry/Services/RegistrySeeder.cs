using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RapidRelief.Api.Features.Registry.Data;
using RapidRelief.Api.Features.Registry.Domain;
using RapidRelief.Api.Infrastructure.SeedData;

namespace RapidRelief.Api.Features.Registry.Services;

public static class RegistrySeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RegistryDbContext>();

        var existingHospitals = (await db.Hospitals.Select(h => h.Id).ToListAsync(ct)).ToHashSet();
        foreach (var h in DhakaSeedData.Hospitals)
        {
            if (!existingHospitals.Contains(h.Id))
            {
                db.Hospitals.Add(new Hospital
                {
                    Id = h.Id,
                    Name = h.Name,
                    Latitude = h.Location.Latitude,
                    Longitude = h.Location.Longitude,
                    TotalBeds = h.TotalBeds,
                    AvailableBeds = h.AvailableBeds,
                    IcuBeds = (int)Math.Round(h.TotalBeds * 0.1),
                    AvailableIcuBeds = (int)Math.Round(h.AvailableBeds * 0.1),
                    HasEmergency = true,
                    Specialties = h.Specialties.ToList(),
                    ContactNumber = "+880 2 5550100",
                    Address = $"{h.Name}, Dhaka",
                    CreatedAtUtc = DhakaSeedData.AnchorUtc,
                    UpdatedAtUtc = DhakaSeedData.AnchorUtc
                });
            }
        }

        var existingVolunteers = (await db.Volunteers.Select(v => v.Id).ToListAsync(ct)).ToHashSet();
        foreach (var v in DhakaSeedData.Volunteers)
        {
            if (!existingVolunteers.Contains(v.Id))
            {
                db.Volunteers.Add(new Volunteer
                {
                    Id = v.Id,
                    UserId = null,
                    FullName = v.Name,
                    Latitude = v.Location?.Latitude,
                    Longitude = v.Location?.Longitude,
                    Skills = v.Skills.ToList(),
                    Status = v.IsAvailable ? "Available" : "Unavailable",
                    ContactNumber = "+880 1555 100000",
                    CreatedAtUtc = DhakaSeedData.AnchorUtc,
                    UpdatedAtUtc = DhakaSeedData.AnchorUtc
                });
            }
        }

        var existingNgos = (await db.Ngos.Select(n => n.Id).ToListAsync(ct)).ToHashSet();
        foreach (var n in DhakaSeedData.Ngos)
        {
            if (!existingNgos.Contains(n.Id))
            {
                db.Ngos.Add(new Ngo
                {
                    Id = n.Id,
                    Name = n.Name,
                    Latitude = DhakaSeedData.DhakaCenter.Latitude,
                    Longitude = DhakaSeedData.DhakaCenter.Longitude,
                    FocusAreas = n.FocusAreas.ToList(),
                    ContactPerson = "Director",
                    ContactEmail = n.ContactEmail,
                    ContactNumber = "+880 2 5550200",
                    CreatedAtUtc = DhakaSeedData.AnchorUtc,
                    UpdatedAtUtc = DhakaSeedData.AnchorUtc
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
