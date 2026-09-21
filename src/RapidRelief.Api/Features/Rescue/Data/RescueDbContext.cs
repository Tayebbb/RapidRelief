using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using RapidRelief.Api.Features.Rescue.Domain;

namespace RapidRelief.Api.Features.Rescue.Data;

/// <summary>
/// Rescue module context owning rescue_* tables and __efmigrationshistory_rescue.
/// </summary>
public sealed class RescueDbContext : DbContext
{
    public const string MigrationsHistoryTableName = "__efmigrationshistory_rescue";

    public RescueDbContext(DbContextOptions<RescueDbContext> options)
        : base(options)
    {
    }

    public DbSet<RescueTeam> Teams => Set<RescueTeam>();
    public DbSet<RescueTeamMember> TeamMembers => Set<RescueTeamMember>();
    public DbSet<RescueMission> Missions => Set<RescueMission>();
    public DbSet<RescueMissionLog> MissionLogs => Set<RescueMissionLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // SQLite (tests) cannot ORDER BY a DateTimeOffset TEXT column — store UTC ticks there.
        var isSqlite = Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite";

        modelBuilder.Entity<RescueTeam>(t =>
        {
            t.ToTable("rescue_teams");
            t.HasKey(x => x.Id);
            t.Property(x => x.TeamName).IsRequired().HasMaxLength(150);
            t.Property(x => x.Specialization).HasMaxLength(100);
            t.Property(x => x.ContactNumber).HasMaxLength(30);
            t.Property(x => x.Status).HasMaxLength(30);
            t.HasIndex(x => x.TeamLeadUserId);

            if (isSqlite)
            {
                t.Property(x => x.CreatedAtUtc).HasConversion(TicksConverter());
                t.Property(x => x.UpdatedAtUtc).HasConversion(TicksConverter());
            }
        });

        modelBuilder.Entity<RescueTeamMember>(m =>
        {
            m.ToTable("rescue_team_members");
            m.HasKey(x => x.Id);
            m.HasOne(x => x.Team)
                .WithMany(t => t.Members)
                .HasForeignKey(x => x.TeamId)
                .OnDelete(DeleteBehavior.Cascade);
            m.HasIndex(x => x.RescuerUserId);

            if (isSqlite)
            {
                m.Property(x => x.JoinedAtUtc).HasConversion(TicksConverter());
            }
        });

        modelBuilder.Entity<RescueMission>(m =>
        {
            m.ToTable("rescue_missions");
            m.HasKey(x => x.Id);
            m.Property(x => x.MissionTitle).IsRequired().HasMaxLength(200);
            m.Property(x => x.Priority).HasMaxLength(30);
            m.Property(x => x.RejectionReason).HasMaxLength(500);
            m.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            m.HasOne(x => x.Team)
                .WithMany(t => t.Missions)
                .HasForeignKey(x => x.AssignedTeamId)
                .OnDelete(DeleteBehavior.Restrict);
            m.HasIndex(x => x.IncidentId);
            m.HasIndex(x => x.Status);

            if (isSqlite)
            {
                m.Property(x => x.AssignedAtUtc).HasConversion(TicksConverter());
                m.Property(x => x.CreatedAtUtc).HasConversion(TicksConverter());
                m.Property(x => x.UpdatedAtUtc).HasConversion(TicksConverter());
                m.Property(x => x.AcceptedAtUtc).HasConversion(NullableTicksConverter());
                m.Property(x => x.StartedAtUtc).HasConversion(NullableTicksConverter());
                m.Property(x => x.OnSceneAtUtc).HasConversion(NullableTicksConverter());
                m.Property(x => x.CompletedAtUtc).HasConversion(NullableTicksConverter());
            }
        });

        modelBuilder.Entity<RescueMissionLog>(l =>
        {
            l.ToTable("rescue_mission_logs");
            l.HasKey(x => x.Id);
            l.Property(x => x.StatusUpdate).HasMaxLength(50);
            l.HasOne(x => x.Mission)
                .WithMany(m => m.Logs)
                .HasForeignKey(x => x.MissionId)
                .OnDelete(DeleteBehavior.Cascade);

            if (isSqlite)
            {
                l.Property(x => x.TimestampUtc).HasConversion(TicksConverter());
            }
        });
    }

    private static ValueConverter<DateTimeOffset, long> TicksConverter() =>
        new(v => v.UtcTicks, v => new DateTimeOffset(v, TimeSpan.Zero));

    private static ValueConverter<DateTimeOffset?, long?> NullableTicksConverter() =>
        new(v => v!.Value.UtcTicks, v => v == null ? null : new DateTimeOffset(v.Value, TimeSpan.Zero));
}
