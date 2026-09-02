using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RapidRelief.Api.Features.Ai.Assistant;
using RapidRelief.Api.Features.Ai.Data;
using RapidRelief.Api.Features.Ai.Domain;
using RapidRelief.Api.Infrastructure.Persistence;

namespace RapidRelief.Api.Tests.Ai.Assistant;

/// <summary>
/// F16 TEST PLAN item 13 — D-048 retention: chat text can describe the user's situation, so
/// it lives 7 days, not F9's 30. Seeded relative to the live clock on purpose.
/// </summary>
public sealed class AssistantRetentionTests : IClassFixture<TestingWebAppFactory>
{
    private readonly TestingWebAppFactory _factory;

    public AssistantRetentionTests(TestingWebAppFactory factory) => _factory = factory;

    private AssistantRetentionWorker Worker() => new(
        _factory.Services.GetRequiredService<IServiceScopeFactory>(),
        new AssistantOptions { RetentionDays = 7 },
        TimeProvider.System,
        NullLogger<AssistantRetentionWorker>.Instance);

    private async Task ResetAsync()
    {
        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AiDbContext>().AssistantMessages.ExecuteDeleteAsync();
    }

    private async Task SeedAsync(int count, TimeSpan age)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AiDbContext>();
        for (var i = 0; i < count; i++)
        {
            db.AssistantMessages.Add(new AssistantMessage
            {
                Id = Guid.NewGuid(),
                SessionId = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                Role = AssistantRole.User,
                Text = $"row {i}",
                CreatedAtUtc = DateTimeOffset.UtcNow - age,
            });
        }
        await db.SaveChangesAsync();
    }

    private async Task<int> CountAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AiDbContext>().AssistantMessages.CountAsync();
    }

    [Fact]
    public async Task Rows_past_the_retention_window_are_deleted_and_fresh_rows_survive()
    {
        await ResetAsync();
        await SeedAsync(3, TimeSpan.FromDays(8));
        await SeedAsync(2, TimeSpan.FromDays(1));

        var deleted = await Worker().SweepAsync(CancellationToken.None);

        Assert.Equal(3, deleted);
        Assert.Equal(2, await CountAsync());
    }

    [Fact]
    public async Task More_rows_than_one_batch_are_swept_in_full()
    {
        await ResetAsync();
        await SeedAsync(AssistantRetentionWorker.BatchSize + 25, TimeSpan.FromDays(30));

        var deleted = await Worker().SweepAsync(CancellationToken.None);

        Assert.Equal(AssistantRetentionWorker.BatchSize + 25, deleted);
        Assert.Equal(0, await CountAsync());
    }

    [Fact]
    public async Task The_sweep_is_skipped_while_the_database_is_degraded()
    {
        await ResetAsync();
        await SeedAsync(3, TimeSpan.FromDays(30));
        var health = _factory.Services.GetRequiredService<DatabaseHealth>();
        try
        {
            health.PostgresAvailable = false;

            var deleted = await Worker().SweepAsync(CancellationToken.None);

            Assert.Equal(0, deleted);
        }
        finally
        {
            health.PostgresAvailable = true;
        }

        Assert.Equal(3, await CountAsync());
    }
}
