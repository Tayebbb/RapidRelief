using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RapidRelief.Api.Features.Registry.Domain;

namespace RapidRelief.Api.Features.Registry.Data;

/// <summary>
/// Registry module context owning registry_* tables and __efmigrationshistory_registry.
/// </summary>
public sealed class RegistryDbContext : DbContext
{
    public const string MigrationsHistoryTableName = "__efmigrationshistory_registry";

    public RegistryDbContext(DbContextOptions<RegistryDbContext> options)
        : base(options)
    {
    }

    public DbSet<Hospital> Hospitals => Set<Hospital>();
    public DbSet<Volunteer> Volunteers => Set<Volunteer>();
    public DbSet<Ngo> Ngos => Set<Ngo>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var isSqlite = Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

        modelBuilder.Entity<Hospital>(h =>
        {
            h.ToTable("registry_hospitals");
            h.HasKey(x => x.Id);
            h.Property(x => x.Name).IsRequired().HasMaxLength(150);
            h.Property(x => x.ContactNumber).HasMaxLength(50);
            h.Property(x => x.Address).HasMaxLength(300);
            h.Ignore(x => x.Location);

            if (isSqlite)
            {
                h.Property(x => x.Specialties).HasConversion(StringListConverter());
                h.Property(x => x.CreatedAtUtc).HasConversion(TicksConverter());
                h.Property(x => x.UpdatedAtUtc).HasConversion(TicksConverter());
            }
        });

        modelBuilder.Entity<Volunteer>(v =>
        {
            v.ToTable("registry_volunteers");
            v.HasKey(x => x.Id);
            v.Property(x => x.FullName).IsRequired().HasMaxLength(150);
            v.Property(x => x.Status).IsRequired().HasMaxLength(50);
            v.Property(x => x.ContactNumber).HasMaxLength(50);
            v.HasIndex(x => x.UserId);
            v.HasIndex(x => x.Status);
            v.Ignore(x => x.Location);

            if (isSqlite)
            {
                v.Property(x => x.Skills).HasConversion(StringListConverter());
                v.Property(x => x.CreatedAtUtc).HasConversion(TicksConverter());
                v.Property(x => x.UpdatedAtUtc).HasConversion(TicksConverter());
            }
        });

        modelBuilder.Entity<Ngo>(n =>
        {
            n.ToTable("registry_ngos");
            n.HasKey(x => x.Id);
            n.Property(x => x.Name).IsRequired().HasMaxLength(150);
            n.Property(x => x.ContactPerson).HasMaxLength(100);
            n.Property(x => x.ContactEmail).HasMaxLength(150);
            n.Property(x => x.ContactNumber).HasMaxLength(50);
            n.Ignore(x => x.Location);

            if (isSqlite)
            {
                n.Property(x => x.FocusAreas).HasConversion(StringListConverter());
                n.Property(x => x.CreatedAtUtc).HasConversion(TicksConverter());
                n.Property(x => x.UpdatedAtUtc).HasConversion(TicksConverter());
            }
        });
    }

    private static ValueConverter<List<string>, string> StringListConverter() =>
        new(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()
        );

    private static ValueConverter<DateTimeOffset, long> TicksConverter() =>
        new(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
}
