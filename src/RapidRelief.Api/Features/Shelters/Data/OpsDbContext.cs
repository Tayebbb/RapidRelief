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
    }
}
