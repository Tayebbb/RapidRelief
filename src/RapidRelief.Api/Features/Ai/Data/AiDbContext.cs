using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Ai.Domain;

namespace RapidRelief.Api.Features.Ai.Data;

/// <summary>
/// F8-owned context (§4.4): ai_ table prefix, own migrations history table, SQLite ticks
/// gate on BOTH DateTimeOffset columns — SnapshotReportedAtUtc appears in time-window
/// WHERE clauses (duplicate detection) and must compare on INTEGER ticks under SQLite.
/// </summary>
public sealed class AiDbContext : DbContext
{
    public const string MigrationsHistoryTableName = "__efmigrationshistory_ai";

    public AiDbContext(DbContextOptions<AiDbContext> options)
        : base(options)
    {
    }

    public DbSet<AiAssessment> Assessments => Set<AiAssessment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AiAssessment>(assessment =>
        {
            assessment.ToTable("ai_assessments"); // feature_ prefix convention (PROJECT-CONTEXT §5)
            assessment.HasKey(a => a.Id);
            assessment.HasIndex(a => a.IncidentId).IsUnique(); // idempotency: one row per incident
            assessment.Property(a => a.Summary).IsRequired().HasMaxLength(200);
            assessment.Property(a => a.Provider).IsRequired().HasMaxLength(32);
            assessment.Property(a => a.ModelName).HasMaxLength(64);
            assessment.Property(a => a.FinishReason).HasMaxLength(32);

            // SampleDbContext ticks gate: SQLite cannot compare DateTimeOffset TEXT columns
            // in SQL; Npgsql stays on native timestamptz.
            if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                assessment.Property(a => a.SnapshotReportedAtUtc).HasConversion(
                    v => v.UtcTicks,
                    v => new DateTimeOffset(v, TimeSpan.Zero));
                assessment.Property(a => a.CreatedAtUtc).HasConversion(
                    v => v.UtcTicks,
                    v => new DateTimeOffset(v, TimeSpan.Zero));
            }
        });
    }
}
