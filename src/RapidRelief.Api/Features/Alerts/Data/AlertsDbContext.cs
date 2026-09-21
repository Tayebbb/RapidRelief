using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Alerts.Domain;

namespace RapidRelief.Api.Features.Alerts.Data;

public sealed class AlertsDbContext(DbContextOptions<AlertsDbContext> options) : DbContext(options)
{
    public const string MigrationsHistoryTableName = "__efmigrationshistory_alerts";

    public DbSet<Alert> Alerts => Set<Alert>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Alert>(alert =>
        {
            alert.ToTable("alerts_alerts");
            alert.HasKey(x => x.Id);
            alert.Property(x => x.Title).IsRequired().HasMaxLength(160);
            alert.Property(x => x.Body).IsRequired().HasMaxLength(500);
            alert.Property(x => x.Severity).HasConversion<string>().HasMaxLength(30);
            alert.Property(x => x.DisasterType).HasConversion<string>().HasMaxLength(40);
            alert.Property(x => x.TargetArea).IsRequired().HasMaxLength(150);
            alert.HasIndex(x => new { x.ExpiresAtUtc, x.RevokedAtUtc });
            alert.HasIndex(x => x.CreatedAtUtc);

            if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                alert.Property(x => x.ExpiresAtUtc).HasConversion(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
                alert.Property(x => x.CreatedAtUtc).HasConversion(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));
                alert.Property(x => x.RevokedAtUtc).HasConversion(v => v.HasValue ? v.Value.UtcTicks : (long?)null, v => v.HasValue ? new DateTimeOffset(v.Value, TimeSpan.Zero) : null);
            }
        });
    }
}
