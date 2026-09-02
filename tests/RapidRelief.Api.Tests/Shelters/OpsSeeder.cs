using Microsoft.Extensions.DependencyInjection;
using RapidRelief.Api.Features.Shelters.Data;
using RapidRelief.Api.Features.Shelters.Domain;
using RapidRelief.Api.Features.Stubs.SeedData;

namespace RapidRelief.Api.Tests.Shelters;

public static class OpsSeeder
{
    public static async Task SeedAsync(IServiceProvider serviceProvider, CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpsDbContext>();

        if (db.Shelters.Any())
        {
            return;
        }

        var sheltersToSeed = DhakaSeedData.Shelters.Select(dto => new Shelter
        {
            Id = dto.Id,
            Name = dto.Name,
            Location = dto.Location,
            Capacity = dto.Capacity,
            CurrentOccupancy = dto.Occupancy,
            Facilities = new List<string> { "Basic" }, // Fallback
            Status = dto.IsOpen ? ShelterStatus.Open : ShelterStatus.Closed
        }).ToList();

        db.Shelters.AddRange(sheltersToSeed);
        await db.SaveChangesAsync(ct);
    }
}
