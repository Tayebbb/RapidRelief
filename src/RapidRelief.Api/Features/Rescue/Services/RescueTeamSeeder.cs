using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Rescue.Data;
using RapidRelief.Api.Features.Rescue.Domain;
using RapidRelief.Api.Infrastructure.SeedData;

namespace RapidRelief.Api.Features.Rescue.Services;

/// <summary>
/// Without seeded teams the operational map's team layer, the government team registry and the
/// dispatch suitability ranking all render empty, which makes the whole rescue side of a demo look
/// broken. These are deterministic demo units (fixed ids, fixed positions, no Random).
///
/// Seeds by id rather than "only when the table is empty": a database that already holds real or
/// ad-hoc teams would otherwise never get the demo set. It only ever inserts missing demo ids and
/// never touches a row it did not create.
/// Disable with Rescue:SeedDemoData=false.
/// </summary>
public static class RescueTeamSeeder
{
    public static async Task SeedAsync(IServiceProvider scopedServices, CancellationToken ct)
    {
        var config = scopedServices.GetRequiredService<IConfiguration>();
        if (!config.GetValue("Rescue:SeedDemoData", true))
        {
            return;
        }

        var db = scopedServices.GetRequiredService<RescueDbContext>();
        var demoIds = DhakaSeedData.RescueTeams.Select(t => t.Id).ToList();
        var existing = await db.Teams
            .Where(t => demoIds.Contains(t.Id))
            .Select(t => t.Id)
            .ToListAsync(ct);

        var missing = DhakaSeedData.RescueTeams.Where(t => !existing.Contains(t.Id)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var now = DhakaSeedData.AnchorUtc;
        var teams = missing.Select(t => new RescueTeam
        {
            Id = t.Id,
            TeamName = t.Name,
            Specialization = t.Speciality,
            ContactNumber = t.ContactNumber,
            Status = t.IsAvailable ? TeamStatus.Available : TeamStatus.OffDuty,
            // No real user leads a demo unit: a synthetic lead id keeps these out of every
            // signed-in rescuer's "my team" lookup until an operator assigns members.
            TeamLeadUserId = t.Id,
            CurrentLatitude = t.BaseLocation.Latitude,
            CurrentLongitude = t.BaseLocation.Longitude,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        }).ToList();

        db.Teams.AddRange(teams);
        await db.SaveChangesAsync(ct);

        scopedServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(RescueTeamSeeder))
            .LogInformation("Seeded {Count} demo rescue teams", teams.Count);
    }
}
