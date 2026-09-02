using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Relief.Domain;

namespace RapidRelief.Api.Features.Relief.Data;

/// <summary>
/// Relief module context owning relief_* tables and __efmigrationshistory_relief.
/// </summary>
public sealed class ReliefDbContext : DbContext
{
    public const string MigrationsHistoryTableName = "__efmigrationshistory_relief";

    public ReliefDbContext(DbContextOptions<ReliefDbContext> options)
        : base(options)
    {
    }

    public DbSet<ReliefRequest> Requests => Set<ReliefRequest>();
    public DbSet<ReliefResource> Resources => Set<ReliefResource>();
    public DbSet<ReliefDispatch> Dispatches => Set<ReliefDispatch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReliefRequest>(r =>
        {
            r.ToTable("relief_requests");
            r.HasKey(x => x.Id);
            r.Property(x => x.ReliefType).IsRequired().HasMaxLength(50);
            r.Property(x => x.UrgencyLevel).HasMaxLength(30);
            r.Property(x => x.DeliveryAddress).HasMaxLength(250);
            r.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            r.HasIndex(x => x.RequesterId);
            r.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<ReliefResource>(res =>
        {
            res.ToTable("relief_resources");
            res.HasKey(x => x.Id);
            res.Property(x => x.Name).IsRequired().HasMaxLength(150);
            res.Property(x => x.Category).HasConversion<string>().HasMaxLength(50);
            res.Property(x => x.Unit).HasMaxLength(30);
            res.Property(x => x.WarehouseLocation).HasMaxLength(200);
            res.HasIndex(x => x.Category);
        });

        modelBuilder.Entity<ReliefDispatch>(d =>
        {
            d.ToTable("relief_dispatches");
            d.HasKey(x => x.Id);
            d.Property(x => x.CarrierOrPartner).HasMaxLength(150);
            d.Property(x => x.Status).HasMaxLength(30);
            d.HasOne(x => x.ReliefRequest)
                .WithMany(r => r.Dispatches)
                .HasForeignKey(x => x.ReliefRequestId)
                .OnDelete(DeleteBehavior.Cascade);
            d.HasOne(x => x.Resource)
                .WithMany()
                .HasForeignKey(x => x.ResourceId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
