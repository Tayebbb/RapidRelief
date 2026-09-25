using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RapidRelief.Api.Features.SafetyZones.Domain;

namespace RapidRelief.Api.Features.SafetyZones.Data;

/// <summary>
/// SafetyZonesDbContext per the F17 blueprint.
/// Owns the __efmigrationshistory_safety history table and safety_ prefix tables.
/// </summary>
public sealed class SafetyZonesDbContext : DbContext
{
    public const string MigrationsHistoryTableName = "__efmigrationshistory_safety";

    public SafetyZonesDbContext(DbContextOptions<SafetyZonesDbContext> options)
        : base(options)
    {
    }

    public DbSet<SafetyZone> SafetyZones => Set<SafetyZone>();
    public DbSet<RoadClosure> RoadClosures => Set<RoadClosure>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var isSqlite = Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

        modelBuilder.Entity<SafetyZone>(zone =>
        {
            zone.ToTable("safety_zones");
            zone.HasKey(z => z.Id);
            zone.Property(z => z.Name).IsRequired().HasMaxLength(150);
            zone.Property(z => z.Description).HasMaxLength(1000);
            zone.Property(z => z.ZoneType).HasConversion<string>().HasMaxLength(50);
            zone.Property(z => z.Severity).HasConversion<string>().HasMaxLength(30);
            zone.Property(z => z.GeometryType).IsRequired().HasMaxLength(30);
            zone.Property(z => z.CoordinatesJson).HasMaxLength(10000);

            zone.HasIndex(z => z.IsActive);
            zone.HasIndex(z => z.ZoneType);
            zone.HasIndex(z => z.CreatedAtUtc);

            if (isSqlite)
            {
                zone.Property(z => z.CreatedAtUtc).HasConversion(TicksConverter());
                zone.Property(z => z.ExpiresAtUtc).HasConversion(NullableTicksConverter());
            }
        });

        modelBuilder.Entity<RoadClosure>(closure =>
        {
            closure.ToTable("safety_road_closures");
            closure.HasKey(c => c.Id);
            closure.Property(c => c.RoadName).IsRequired().HasMaxLength(150);
            closure.Property(c => c.Description).HasMaxLength(1000);
            closure.Property(c => c.Severity).HasConversion<string>().HasMaxLength(50);
            closure.Property(c => c.BlockedReason).IsRequired().HasMaxLength(500);
            closure.Property(c => c.AlternateRouteAdvice).HasMaxLength(1000);
            closure.Property(c => c.CoordinatesJson).HasMaxLength(10000);

            closure.HasIndex(c => c.IsActive);
            closure.HasIndex(c => c.Severity);
            closure.HasIndex(c => c.ReportedAtUtc);

            if (isSqlite)
            {
                closure.Property(c => c.ReportedAtUtc).HasConversion(TicksConverter());
                closure.Property(c => c.EstimatedReopenUtc).HasConversion(NullableTicksConverter());
            }
        });
    }

    private static ValueConverter<DateTimeOffset, long> TicksConverter() =>
        new(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));

    private static ValueConverter<DateTimeOffset?, long?> NullableTicksConverter() =>
        new(v => v.HasValue ? v.Value.UtcTicks : null, v => v.HasValue ? new DateTimeOffset(v.Value, TimeSpan.Zero) : null);
}
