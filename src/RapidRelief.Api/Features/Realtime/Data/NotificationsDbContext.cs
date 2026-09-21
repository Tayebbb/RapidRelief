using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Realtime.Domain;

namespace RapidRelief.Api.Features.Realtime.Data;

/// <summary>
/// F9-owned context (§4.4) copying the AiDbContext pattern: notifications_ table prefix
/// (D-042), own migrations history table, and the SQLite ticks gate on both DateTimeOffset
/// columns — CreatedAtUtc drives keyset paging/retention and ReadAtUtc is written per read.
/// </summary>
public sealed class NotificationsDbContext : DbContext
{
    public const string MigrationsHistoryTableName = "__efmigrationshistory_notifications";

    public NotificationsDbContext(DbContextOptions<NotificationsDbContext> options)
        : base(options)
    {
    }

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<NotificationRead> Reads => Set<NotificationRead>();

    public DbSet<BroadcastAlert> BroadcastAlerts => Set<BroadcastAlert>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Notification>(notification =>
        {
            notification.ToTable("notifications_notification");
            notification.HasKey(n => n.Id);
            notification.Property(n => n.Audience).IsRequired().HasMaxLength(8);
            notification.Property(n => n.Role).HasMaxLength(16);
            notification.Property(n => n.Topic).IsRequired().HasMaxLength(Notification.MaxTopicChars);
            notification.Property(n => n.Summary).IsRequired().HasMaxLength(Notification.MaxSummaryChars);
            notification.Property(n => n.PayloadJson).IsRequired().HasMaxLength(Notification.MaxPayloadChars);

            notification.HasIndex(n => new { n.CreatedAtUtc, n.Id }); // D-038 keyset
            notification.HasIndex(n => new { n.Audience, n.Role, n.CreatedAtUtc }); // fan-out filter
            notification.HasIndex(n => new { n.UserId, n.CreatedAtUtc }); // targeted rows

            if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                notification.Property(n => n.CreatedAtUtc).HasConversion(
                    v => v.UtcTicks,
                    v => new DateTimeOffset(v, TimeSpan.Zero));
            }
        });

        modelBuilder.Entity<NotificationRead>(read =>
        {
            read.ToTable("notifications_read");
            read.HasKey(r => new { r.NotificationId, r.UserId });
            read.HasIndex(r => new { r.UserId, r.NotificationId });
            read.HasOne<Notification>()
                .WithMany()
                .HasForeignKey(r => r.NotificationId)
                .OnDelete(DeleteBehavior.Cascade); // same context — allowed (§4.3 bans CROSS-module FKs)

            if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                read.Property(r => r.ReadAtUtc).HasConversion(
                    v => v.UtcTicks,
                    v => new DateTimeOffset(v, TimeSpan.Zero));
            }
        });

        modelBuilder.Entity<BroadcastAlert>(b =>
        {
            b.ToTable("notifications_broadcasts");
            b.HasKey(x => x.Id);
            b.Property(x => x.Headline).IsRequired().HasMaxLength(200);
            b.Property(x => x.TargetArea).HasMaxLength(150);
            b.Property(x => x.Severity).HasMaxLength(30);
        });
    }
}
