using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RapidRelief.Api.Features.Audit.Domain;

namespace RapidRelief.Api.Features.Audit.Data;

/// <summary>Audit module context owning audit_entries and __efmigrationshistory_audit (F14, D-097).</summary>
public sealed class AuditDbContext : DbContext
{
    public const string MigrationsHistoryTableName = "__efmigrationshistory_audit";

    public AuditDbContext(DbContextOptions<AuditDbContext> options)
        : base(options)
    {
    }

    public DbSet<AuditEntry> Entries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // SQLite (tests) cannot ORDER BY a DateTimeOffset TEXT column — store UTC ticks there.
        var isSqlite = Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

        modelBuilder.Entity<AuditEntry>(e =>
        {
            e.ToTable("audit_entries");
            e.HasKey(x => x.Id);
            e.Property(x => x.ActorName).HasMaxLength(200);
            e.Property(x => x.ActorRole).HasMaxLength(50);
            e.Property(x => x.Action).IsRequired().HasMaxLength(80);
            e.Property(x => x.EntityType).IsRequired().HasMaxLength(60);
            e.Property(x => x.EntityId).HasMaxLength(80);
            e.Property(x => x.Summary).HasMaxLength(500);
            e.Property(x => x.Result).HasMaxLength(80);
            e.Property(x => x.Source).HasMaxLength(20);
            e.HasIndex(x => x.OccurredAtUtc);
            e.HasIndex(x => x.ActorId);
            e.HasIndex(x => new { x.EntityType, x.EntityId });

            if (isSqlite)
            {
                e.Property(x => x.OccurredAtUtc).HasConversion(TicksConverter());
            }
        });

        base.OnModelCreating(modelBuilder);
    }

    private static ValueConverter<DateTimeOffset, long> TicksConverter() =>
        new(v => v.UtcDateTime.Ticks, v => new DateTimeOffset(v, TimeSpan.Zero));
}
