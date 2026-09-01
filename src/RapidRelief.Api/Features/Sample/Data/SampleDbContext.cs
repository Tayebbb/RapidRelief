using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Sample.Domain;

namespace RapidRelief.Api.Features.Sample.Data;

/// <summary>
/// The ONE concrete F0 context (D-007) proving the per-feature pattern: feature_ table
/// prefix, own migrations history table, --context/--output-dir migration workflow.
/// </summary>
public sealed class SampleDbContext : DbContext
{
    /// <summary>Per-context history table — every future context declares its own (§4.4).</summary>
    public const string MigrationsHistoryTableName = "__efmigrationshistory_sample";

    public SampleDbContext(DbContextOptions<SampleDbContext> options)
        : base(options)
    {
    }

    public DbSet<Ping> Pings => Set<Ping>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ping>(ping =>
        {
            ping.ToTable("sample_pings"); // feature_ prefix convention (PROJECT-CONTEXT §5)
            ping.HasKey(p => p.Id);
            ping.Property(p => p.Message).IsRequired().HasMaxLength(500);

            // Blueprint risk 4: provider-specific config must be provider-gated. SQLite (tests)
            // cannot ORDER BY DateTimeOffset TEXT columns, so store UTC ticks there; Npgsql
            // stays on native timestamptz.
            if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                ping.Property(p => p.CreatedAtUtc).HasConversion(
                    v => v.UtcTicks,
                    v => new DateTimeOffset(v, TimeSpan.Zero));
            }
        });
    }
}
