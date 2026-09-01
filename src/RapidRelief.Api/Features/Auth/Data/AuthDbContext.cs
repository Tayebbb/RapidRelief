using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Auth.Domain;

namespace RapidRelief.Api.Features.Auth.Data;

/// <summary>
/// F1-owned Identity context (blueprint B3): auth_* tables, own migrations history table,
/// SQLite ticks gate on RefreshToken date columns (they appear in SQL WHERE clauses).
/// </summary>
public sealed class AuthDbContext : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>
{
    public const string MigrationsHistoryTableName = "__efmigrationshistory_auth";

    public AuthDbContext(DbContextOptions<AuthDbContext> options)
        : base(options)
    {
    }

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder); // MUST be first — Identity schema/indexes (blueprint risk 2)

        builder.Entity<AppUser>(user =>
        {
            user.ToTable("auth_users"); // feature_ prefix convention (PROJECT-CONTEXT §5)
            user.Property(x => x.DisplayName).IsRequired().HasMaxLength(100);
            user.Property(x => x.EmergencyContact).HasMaxLength(100);
            user.Property(x => x.PhotoPath).HasMaxLength(260);
        });
        builder.Entity<IdentityRole<Guid>>().ToTable("auth_roles");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("auth_user_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("auth_user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("auth_user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("auth_user_tokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("auth_role_claims");

        builder.Entity<RefreshToken>(token =>
        {
            token.ToTable("auth_refresh_tokens");
            token.HasKey(x => x.Id);
            token.Property(x => x.TokenHash).IsRequired().HasMaxLength(64);
            token.HasIndex(x => x.TokenHash).IsUnique();
            token.HasIndex(x => x.UserId); // non-unique — revoke-all scans
            token.Property(x => x.SecurityStampAtIssue).IsRequired().HasMaxLength(100);
            token.Property(x => x.ReplacedByTokenHash).HasMaxLength(64);
            // Two racing rotations both read RevokedAtUtc == null; the losing UPDATE must throw
            // (DbUpdateConcurrencyException) so TokenService can treat it as reuse (D-014).
            token.Property(x => x.RevokedAtUtc).IsConcurrencyToken();
            token.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

            // SampleDbContext ticks gate: SQLite cannot compare DateTimeOffset TEXT columns in
            // WHERE clauses; Npgsql stays on native timestamptz. Identity's LockoutEnd gets NO
            // gate — it must never be compared inside SQL (compute IsLocked in memory, B7).
            if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                token.Property(x => x.CreatedAtUtc).HasConversion(
                    v => v.UtcTicks,
                    v => new DateTimeOffset(v, TimeSpan.Zero));
                token.Property(x => x.ExpiresAtUtc).HasConversion(
                    v => v.UtcTicks,
                    v => new DateTimeOffset(v, TimeSpan.Zero));
                token.Property(x => x.RevokedAtUtc).HasConversion(
                    v => v.HasValue ? v.Value.UtcTicks : (long?)null,
                    v => v.HasValue ? new DateTimeOffset(v.Value, TimeSpan.Zero) : null);
            }
        });
    }
}
