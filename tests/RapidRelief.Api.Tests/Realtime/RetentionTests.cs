using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RapidRelief.Api.Features.Realtime.Data;
using RapidRelief.Api.Features.Realtime.Domain;
using RapidRelief.Api.Features.Realtime.Pipeline;
using RapidRelief.Api.Infrastructure.Persistence;

namespace RapidRelief.Api.Tests.Realtime;

/// <summary>D-034 — the retention sweep bounds the table; read rows cascade with their notification.</summary>
public sealed class RetentionTests : IClassFixture<TestingWebAppFactory>
{
    private readonly TestingWebAppFactory _factory;

    public RetentionTests(TestingWebAppFactory factory) => _factory = factory;

    private NotificationRetentionWorker Worker => _factory.Services
        .GetServices<IHostedService>()
        .OfType<NotificationRetentionWorker>()
        .Single();

    private async Task SeedAsync(params (Guid Id, DateTimeOffset CreatedAt, bool Read)[] rows)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        foreach (var (id, createdAt, read) in rows)
        {
            db.Notifications.Add(new Notification
            {
                Id = id,
                Audience = NotificationAudience.All,
                Topic = "retention.row",
                Summary = "retention",
                PayloadJson = "{}",
                CreatedAtUtc = createdAt,
            });
            if (read)
            {
                db.Reads.Add(new NotificationRead
                {
                    NotificationId = id,
                    UserId = RealtimeTestSupport.CitizenId,
                    ReadAtUtc = createdAt,
                });
            }
        }
        await db.SaveChangesAsync();
    }

    private async Task<(int Notifications, int Reads)> CountsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        return (await db.Notifications.CountAsync(), await db.Reads.CountAsync());
    }

    [Fact]
    public async Task Sweep_deletes_expired_rows_cascades_reads_and_keeps_recent_ones()
    {
        await RealtimeTestSupport.ResetAsync(_factory.Services);
        var now = DateTimeOffset.UtcNow;
        var expired = Guid.NewGuid();
        var fresh = Guid.NewGuid();
        await SeedAsync(
            (expired, now.AddDays(-31), Read: true),
            (fresh, now.AddDays(-29), Read: true));

        var deleted = await Worker.SweepAsync(CancellationToken.None);

        Assert.Equal(1, deleted);
        var (notifications, reads) = await CountsAsync();
        Assert.Equal(1, notifications);
        Assert.Equal(1, reads);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        Assert.True(await db.Notifications.AnyAsync(n => n.Id == fresh));
        Assert.False(await db.Reads.AnyAsync(r => r.NotificationId == expired));
    }

    [Fact]
    public async Task Sweep_deletes_more_rows_than_fit_in_one_batch()
    {
        await RealtimeTestSupport.ResetAsync(_factory.Services);
        var now = DateTimeOffset.UtcNow;
        var rows = Enumerable.Range(0, NotificationRetentionWorker.BatchSize + 3)
            .Select(i => (Guid.NewGuid(), now.AddDays(-40).AddSeconds(i), false))
            .ToArray();
        await SeedAsync(rows);

        var deleted = await Worker.SweepAsync(CancellationToken.None);

        Assert.Equal(rows.Length, deleted);
        Assert.Equal(0, (await CountsAsync()).Notifications);
    }

    [Fact]
    public async Task Sweep_is_skipped_while_the_database_is_degraded()
    {
        await RealtimeTestSupport.ResetAsync(_factory.Services);
        await SeedAsync((Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-99), false));
        var health = _factory.Services.GetRequiredService<DatabaseHealth>();
        int deleted;
        try
        {
            health.PostgresAvailable = false;
            deleted = await Worker.SweepAsync(CancellationToken.None);
        }
        finally
        {
            health.PostgresAvailable = true;
        }

        Assert.Equal(0, deleted);
        Assert.Equal(1, (await CountsAsync()).Notifications);
    }
}
