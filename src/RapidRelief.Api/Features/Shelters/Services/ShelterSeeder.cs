using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Shelters.Data;
using RapidRelief.Api.Features.Shelters.Domain;
using RapidRelief.Api.Infrastructure.SeedData;

namespace RapidRelief.Api.Features.Shelters.Services;

/// <summary>
/// The real <see cref="ShelterReadService"/> displaces the F0 stub, so an empty ops_shelters table
/// silently emptied the finder, the AI recommendation and the assistant's context. Seeding the same
/// Dhaka dataset the stub used keeps those paths meaningful on a fresh database.
/// Runs only while the table is empty; disable with Shelters:SeedDemoData=false.
/// </summary>
public static class ShelterSeeder
{
    public static async Task SeedAsync(IServiceProvider scopedServices, CancellationToken ct)
    {
        var config = scopedServices.GetRequiredService<IConfiguration>();
        if (!config.GetValue("Shelters:SeedDemoData", true))
        {
            return;
        }

        var db = scopedServices.GetRequiredService<OpsDbContext>();
        if (await db.Shelters.AnyAsync(ct))
        {
            return;
        }

        var shelters = DhakaSeedData.Shelters.Select(dto => new Shelter
        {
            Id = dto.Id,
            Name = dto.Name,
            Location = dto.Location,
            Capacity = dto.Capacity,
            CurrentOccupancy = dto.Occupancy,
            Facilities = ["Water", "Medical", "Food"],
            Status = dto.IsOpen ? ShelterStatus.Open : ShelterStatus.Closed,
        }).ToList();

        db.Shelters.AddRange(shelters);
        await db.SaveChangesAsync(ct);

        scopedServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(ShelterSeeder))
            .LogInformation("Seeded {Count} demo shelters into an empty ops_shelters table", shelters.Count);
    }
}
