using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RapidRelief.Api.Infrastructure.Modules;
using RapidRelief.Api.Infrastructure.Persistence;

namespace RapidRelief.Api.Tests.Persistence;

/// <summary>
/// D-005 unit spec: 3 attempts with backoff per module, LogError + DatabaseHealth=false on
/// total failure, NEVER throws, one module's failure never blocks another module's migration.
/// </summary>
public sealed class MigrationRunnerTests
{
    private static ServiceProvider BuildServices() =>
        new ServiceCollection()
            .AddLogging()
            .AddSingleton<DatabaseHealth>()
            .BuildServiceProvider();

    [Fact]
    public async Task All_modules_succeeding_sets_postgres_available_true()
    {
        await using var services = BuildServices();
        var module = new RecordingModule(failuresBeforeSuccess: 0);

        await MigrationRunner.RunAsync(services, [module], TimeSpan.Zero);

        Assert.True(services.GetRequiredService<DatabaseHealth>().PostgresAvailable);
        Assert.Equal(1, module.Attempts);
    }

    [Fact]
    public async Task Module_failing_twice_then_succeeding_on_third_attempt_reports_healthy()
    {
        await using var services = BuildServices();
        var module = new RecordingModule(failuresBeforeSuccess: 2);

        await MigrationRunner.RunAsync(services, [module], TimeSpan.Zero);

        Assert.True(services.GetRequiredService<DatabaseHealth>().PostgresAvailable);
        Assert.Equal(3, module.Attempts);
    }

    [Fact]
    public async Task Module_always_failing_stops_after_three_attempts_sets_degraded_and_never_throws()
    {
        await using var services = BuildServices();
        var module = new RecordingModule(failuresBeforeSuccess: int.MaxValue);

        await MigrationRunner.RunAsync(services, [module], TimeSpan.Zero);

        Assert.False(services.GetRequiredService<DatabaseHealth>().PostgresAvailable);
        Assert.Equal(3, module.Attempts);
    }

    [Fact]
    public async Task One_failing_module_does_not_prevent_other_modules_from_migrating()
    {
        await using var services = BuildServices();
        var failing = new RecordingModule(failuresBeforeSuccess: int.MaxValue);
        var healthy = new RecordingModule(failuresBeforeSuccess: 0);

        await MigrationRunner.RunAsync(services, [failing, healthy], TimeSpan.Zero);

        Assert.Equal(1, healthy.Attempts);
        Assert.False(services.GetRequiredService<DatabaseHealth>().PostgresAvailable);
    }

    private sealed class RecordingModule : IFeatureModule
    {
        private readonly int _failuresBeforeSuccess;

        public RecordingModule(int failuresBeforeSuccess) => _failuresBeforeSuccess = failuresBeforeSuccess;

        public int Attempts { get; private set; }

        public string Name => "Recording";

        public void AddModule(IServiceCollection services, IConfiguration config, IHostEnvironment env)
        {
        }

        public void MapEndpoints(IEndpointRouteBuilder endpoints)
        {
        }

        public Task MigrateAsync(IServiceProvider scopedServices, CancellationToken ct)
        {
            Attempts++;
            return Attempts <= _failuresBeforeSuccess
                ? throw new InvalidOperationException("simulated migration failure")
                : Task.CompletedTask;
        }
    }
}
