using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Shelters.Domain;

namespace RapidRelief.Api.Features.Shelters.Data;

/// <summary>
/// OpsDbContext per the F3 blueprint.
/// Owns the __efmigrationshistory_ops history table and the ops_ prefix for tables.
/// </summary>
public sealed class OpsDbContext : DbContext
{
    public const string MigrationsHistoryTableName = "__efmigrationshistory_ops";

    public OpsDbContext(DbContextOptions<OpsDbContext> options)
        : base(options)
    {
    }

    public DbSet<Shelter> Shelters => Set<Shelter>();
    public DbSet<ShelterSupply> ShelterSupplies => Set<ShelterSupply>();
    public DbSet<SafetyZone> SafetyZones => Set<SafetyZone>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Shelter>(shelter =>
        {
            // feature_ prefix convention (PROJECT-CONTEXT §5)
            shelter.ToTable("ops_shelters");
            
            shelter.HasKey(s => s.Id);
            shelter.Property(s => s.Name).IsRequired().HasMaxLength(100);
            
            shelter.OwnsOne(s => s.Location);
            
            // SQLite (tests) cannot natively store arrays/jsonb out of the box without mapping
            // For cross-provider portability, we use a simple JSON string conversion for SQLite.
            if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                shelter.Property(s => s.Facilities).HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()
                );
            }
        });

        modelBuilder.Entity<ShelterSupply>(supply =>
        {
            supply.ToTable("ops_shelter_supplies");
            supply.HasKey(s => s.Id);
            supply.Property(s => s.SupplyType).IsRequired().HasMaxLength(100);
            supply.Property(s => s.Unit).HasMaxLength(30);
            supply.HasIndex(s => s.ShelterId);
        });

        modelBuilder.Entity<SafetyZone>(zone =>
        {
            zone.ToTable("ops_safety_zones");
            zone.HasKey(z => z.Id);
            zone.Property(z => z.Name).IsRequired().HasMaxLength(150);
            zone.Property(z => z.ZoneType).HasMaxLength(50);
            zone.Property(z => z.RiskLevel).HasMaxLength(30);
        });
    }
}
