using Microsoft.EntityFrameworkCore;
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
        modelBuilder.Entity<RescueTeam>(t =>
        {
            t.ToTable("rescue_teams");
            t.HasKey(x => x.Id);
            t.Property(x => x.TeamName).IsRequired().HasMaxLength(150);
            t.Property(x => x.Specialization).HasMaxLength(100);
            t.Property(x => x.ContactNumber).HasMaxLength(30);
            t.Property(x => x.Status).HasMaxLength(30);
            t.HasIndex(x => x.TeamLeadUserId);
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
        });

        modelBuilder.Entity<RescueMission>(m =>
        {
            m.ToTable("rescue_missions");
            m.HasKey(x => x.Id);
            m.Property(x => x.MissionTitle).IsRequired().HasMaxLength(200);
            m.Property(x => x.Priority).HasMaxLength(30);
            m.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            m.HasOne(x => x.Team)
                .WithMany(t => t.Missions)
                .HasForeignKey(x => x.AssignedTeamId)
                .OnDelete(DeleteBehavior.Restrict);
            m.HasIndex(x => x.IncidentId);
            m.HasIndex(x => x.Status);
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
        });
    }
}
