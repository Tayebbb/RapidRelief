using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Incidents.Domain;

namespace RapidRelief.Api.Features.Incidents.Data;

/// <summary>
/// Incidents module context owning incidents_* tables and __efmigrationshistory_incidents.
/// </summary>
public sealed class IncidentsDbContext : DbContext
{
    public const string MigrationsHistoryTableName = "__efmigrationshistory_incidents";

    public IncidentsDbContext(DbContextOptions<IncidentsDbContext> options)
        : base(options)
    {
    }

    public DbSet<IncidentReport> Reports => Set<IncidentReport>();
    public DbSet<IncidentMedia> Media => Set<IncidentMedia>();
    public DbSet<IncidentStatusHistory> StatusHistory => Set<IncidentStatusHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IncidentReport>(r =>
        {
            r.ToTable("incidents_reports");
            r.HasKey(x => x.Id);
            r.Property(x => x.Title).IsRequired().HasMaxLength(200);
            r.Property(x => x.AddressOrArea).HasMaxLength(250);
            r.Property(x => x.DisasterType).HasConversion<string>().HasMaxLength(50);
            r.Property(x => x.Severity).HasConversion<string>().HasMaxLength(30);
            r.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            r.HasIndex(x => x.ReporterId);
            r.HasIndex(x => x.Status);
            r.HasIndex(x => x.DisasterType);
            r.HasIndex(x => x.CreatedAtUtc);
        });

        modelBuilder.Entity<IncidentMedia>(m =>
        {
            m.ToTable("incidents_media");
            m.HasKey(x => x.Id);
            m.Property(x => x.FileUrl).IsRequired().HasMaxLength(500);
            m.Property(x => x.MediaType).HasMaxLength(50);
            m.Property(x => x.Caption).HasMaxLength(250);
            m.HasOne(x => x.Incident)
                .WithMany(i => i.Media)
                .HasForeignKey(x => x.IncidentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IncidentStatusHistory>(h =>
        {
            h.ToTable("incidents_status_history");
            h.HasKey(x => x.Id);
            h.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(30);
            h.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(30);
            h.HasOne(x => x.Incident)
                .WithMany(i => i.StatusHistory)
                .HasForeignKey(x => x.IncidentId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
