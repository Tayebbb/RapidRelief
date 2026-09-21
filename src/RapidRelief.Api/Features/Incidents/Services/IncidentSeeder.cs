using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Incidents.Data;
using RapidRelief.Api.Features.Incidents.Domain;
using RapidRelief.Api.Infrastructure.SeedData;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Features.Incidents.Services;

/// <summary>
/// The real <see cref="Services.IncidentReadService"/> displaces the F0 stub, so a fresh database
/// would leave the map, the rescue queue and the AI recommendation endpoints empty. Seeding the
/// same deterministic Dhaka dataset (identical ids) keeps every demo surface meaningful.
/// Runs only while the table is empty; disable with Incidents:SeedDemoData=false.
/// </summary>
public static class IncidentSeeder
{
    /// <summary>Demo rows belong to a synthetic reporter so they never appear in a real citizen's list.</summary>
    public static readonly Guid DemoReporterId = Guid.Parse("dddddddd-0000-0000-0000-000000000001");

    public static async Task SeedAsync(IServiceProvider scopedServices, CancellationToken ct)
    {
        var config = scopedServices.GetRequiredService<IConfiguration>();
        if (!config.GetValue("Incidents:SeedDemoData", true))
        {
            return;
        }

        var db = scopedServices.GetRequiredService<IncidentsDbContext>();
        if (await db.Reports.AnyAsync(ct))
        {
            return;
        }

        var reports = DhakaSeedData.Incidents.Select(dto => new IncidentReport
        {
            Id = dto.Id,
            ReporterId = DemoReporterId,
            Title = dto.Summary,
            Description = dto.Summary,
            DisasterType = dto.Type,
            Severity = dto.Severity,
            Status = dto.Status,
            Latitude = dto.Location.Latitude,
            Longitude = dto.Location.Longitude,
            AddressOrArea = "Dhaka demo dataset",
            AffectedPeopleCount = 0,
            IsSos = dto.IsSos,
            PriorityScore = dto.PriorityScore,
            AiSummary = dto.Summary,
            CreatedAtUtc = dto.ReportedAtUtc,
            UpdatedAtUtc = dto.ReportedAtUtc,
            ResolvedAtUtc = dto.Status == IncidentStatus.Resolved ? dto.ReportedAtUtc : null,
        }).ToList();

        db.Reports.AddRange(reports);
        await db.SaveChangesAsync(ct);

        scopedServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(IncidentSeeder))
            .LogInformation("Seeded {Count} demo incidents into an empty incidents_reports table", reports.Count);
    }
}
