using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using RapidRelief.Api.Features.Realtime.Data;
using RapidRelief.Api.Features.Realtime.Domain;
using RapidRelief.Api.Features.Realtime.Endpoints;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Tests.Realtime;

/// <summary>
/// read-all writes one batch of read rows. If another tab marks one of them read in between,
/// the whole batch fails on the composite primary key — recovery must be ONE re-query plus ONE
/// batch retry (a per-row loop would be up to 1000 round-trips on a rate-limited endpoint).
/// </summary>
public sealed class ReadAllConcurrencyTests : IDisposable
{
    private const int Seeded = 12;

    private readonly SqliteConnection _anchor;
    private readonly string _connectionString;

    public ReadAllConcurrencyTests()
    {
        _connectionString = $"Data Source=read-all-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False";
        _anchor = new SqliteConnection(_connectionString);
        _anchor.Open();
        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _anchor.Dispose();

    [Fact]
    public async Task A_concurrent_read_is_recovered_with_a_single_batch_retry()
    {
        var userId = Guid.NewGuid();
        var ids = await SeedAsync();
        var conflicting = ids[3];
        var interceptor = new ConcurrentReadInjector(() => MarkReadOutOfBandAsync(conflicting, userId));

        int marked;
        using (var db = CreateContext(interceptor))
        {
            marked = await NotificationEndpoints.MarkAllVisibleReadAsync(
                db, userId, [Roles.Citizen], DateTimeOffset.UtcNow,
                NullLogger.Instance, CancellationToken.None);
        }

        Assert.Equal(1, interceptor.Injections);
        Assert.Equal(2, interceptor.SaveAttempts); // the failed batch and exactly one retry
        Assert.Equal(Seeded - 1, marked);          // the row someone else marked is not ours to claim
        using var verify = CreateContext();
        Assert.Equal(Seeded, await verify.Reads.CountAsync(r => r.UserId == userId));
    }

    [Fact]
    public async Task An_uncontended_read_all_marks_everything_in_one_save()
    {
        var userId = Guid.NewGuid();
        await SeedAsync();
        var interceptor = new ConcurrentReadInjector(() => Task.CompletedTask);

        int marked;
        using (var db = CreateContext(interceptor))
        {
            marked = await NotificationEndpoints.MarkAllVisibleReadAsync(
                db, userId, [Roles.Citizen], DateTimeOffset.UtcNow,
                NullLogger.Instance, CancellationToken.None);
        }

        Assert.Equal(1, interceptor.SaveAttempts);
        Assert.Equal(Seeded, marked);
    }

    private NotificationsDbContext CreateContext(IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<NotificationsDbContext>().UseSqlite(_connectionString);
        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        return new NotificationsDbContext(builder.Options);
    }

    private async Task<List<Guid>> SeedAsync()
    {
        using var db = CreateContext();
        var ids = new List<Guid>(Seeded);
        for (var i = 0; i < Seeded; i++)
        {
            var id = Guid.NewGuid();
            ids.Add(id);
            db.Notifications.Add(new Notification
            {
                Id = id,
                Audience = NotificationAudience.All,
                Topic = "readall.row",
                Summary = "read all",
                PayloadJson = "{}",
                CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-i),
            });
        }

        await db.SaveChangesAsync();
        return ids;
    }

    /// <summary>Another tab, on its own connection, marking one of the batch's rows read.</summary>
    private async Task MarkReadOutOfBandAsync(Guid notificationId, Guid userId)
    {
        using var db = CreateContext();
        db.Reads.Add(new NotificationRead
        {
            NotificationId = notificationId,
            UserId = userId,
            ReadAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Fires the injected write once, just before the first batch hits the database.</summary>
    private sealed class ConcurrentReadInjector : ISaveChangesInterceptor
    {
        private readonly Func<Task> _inject;

        public ConcurrentReadInjector(Func<Task> inject) => _inject = inject;

        public int SaveAttempts { get; private set; }

        public int Injections { get; private set; }

        public async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            SaveAttempts++;
            if (SaveAttempts == 1)
            {
                Injections++;
                await _inject();
            }

            return result;
        }
    }
}
