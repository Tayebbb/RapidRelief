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

    /// <summary>F16 server-owned conversation turns (D-048).</summary>
    public DbSet<AssistantMessage> AssistantMessages => Set<AssistantMessage>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AssistantMessage>(message =>
        {
            message.ToTable("ai_assistant_messages");
            message.HasKey(m => m.Id);
            // Every POST/GET filters UserId AND SessionId then orders by time — one covering index.
            message.HasIndex(m => new { m.UserId, m.SessionId, m.CreatedAtUtc });
            // History read + window (SessionId) and ownership filter + retention sweep (UserId).
            message.HasIndex(m => new { m.SessionId, m.CreatedAtUtc });
            message.HasIndex(m => new { m.UserId, m.CreatedAtUtc });
            message.Property(m => m.Text).IsRequired().HasMaxLength(4000);
            message.Property(m => m.Provider).HasMaxLength(32);

            if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                // ORDER BY / retention WHERE both hit this column — it must compare as ticks.
                message.Property(m => m.CreatedAtUtc).HasConversion(
                    v => v.UtcTicks,
                    v => new DateTimeOffset(v, TimeSpan.Zero));
            }
        });

        modelBuilder.Entity<AiAssessment>(assessment =>
        {
            assessment.ToTable("ai_assessments"); // feature_ prefix convention (PROJECT-CONTEXT §5)
            assessment.HasKey(a => a.Id);
            assessment.HasIndex(a => a.IncidentId).IsUnique(); // idempotency: one row per incident
            // Duplicate-detection scan (D-022): type + time window filter in SQL.
            assessment.HasIndex(a => new { a.SnapshotType, a.SnapshotReportedAtUtc });
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

        modelBuilder.Entity<AuditLog>(log =>
        {
            log.ToTable("audit_logs");
            log.HasKey(x => x.Id);
            log.Property(x => x.Action).IsRequired().HasMaxLength(100);
            log.Property(x => x.EntityType).HasMaxLength(100);
            log.Property(x => x.EntityId).HasMaxLength(100);
            log.Property(x => x.IpAddress).HasMaxLength(50);
            log.HasIndex(x => x.UserId);
            log.HasIndex(x => x.TimestampUtc);
        });
    }
}
