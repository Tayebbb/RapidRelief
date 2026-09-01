using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RapidRelief.Api.Infrastructure.Eventing;
using RapidRelief.Shared.Contracts.Eventing;

namespace RapidRelief.Api.Tests.Eventing;

public sealed class InProcessEventBusTests
{
    private sealed record TestEvent : EventBase;

    /// <summary>Shared per-scope call log the probe handlers write into.</summary>
    private sealed class CallLog
    {
        private readonly List<string> _entries = [];
        public IReadOnlyList<string> Entries { get { lock (_entries) { return _entries.ToArray(); } } }
        public void Add(string entry) { lock (_entries) { _entries.Add(entry); } }
        public int Count => Entries.Count;
    }

    private sealed class FirstHandler(CallLog log) : IEventHandler<TestEvent>
    {
        public async Task HandleAsync(TestEvent evt, CancellationToken ct = default)
        {
            log.Add("first:start");
            await Task.Delay(30, ct);
            log.Add("first:end");
        }
    }

    private sealed class SecondHandler(CallLog log) : IEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent evt, CancellationToken ct = default)
        {
            log.Add("second:start");
            log.Add("second:end");
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHandler : IEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent evt, CancellationToken ct = default)
            => throw new InvalidOperationException("boom");
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, Exception? Exception, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, exception, formatter(state, exception)));
    }

    private static ServiceProvider BuildProvider(CapturingLogger<InProcessEventBus> logger, Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<InProcessEventBus>>(logger);
        services.AddScoped<CallLog>();
        services.AddScoped<IEventBus, InProcessEventBus>();
        configure(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Fact]
    public async Task PublishAsync_invokes_all_registered_handlers()
    {
        var logger = new CapturingLogger<InProcessEventBus>();
        await using var provider = BuildProvider(logger, s =>
        {
            s.AddScoped<IEventHandler<TestEvent>, FirstHandler>();
            s.AddScoped<IEventHandler<TestEvent>, SecondHandler>();
        });
        using var scope = provider.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        await bus.PublishAsync(new TestEvent());

        var log = scope.ServiceProvider.GetRequiredService<CallLog>();
        Assert.Contains("first:end", log.Entries);
        Assert.Contains("second:end", log.Entries);
    }

    [Fact]
    public async Task PublishAsync_with_zero_handlers_completes_silently()
    {
        var logger = new CapturingLogger<InProcessEventBus>();
        await using var provider = BuildProvider(logger, _ => { });
        using var scope = provider.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        await bus.PublishAsync(new TestEvent());

        Assert.DoesNotContain(logger.Entries, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task PublishAsync_when_first_handler_throws_second_still_runs_and_error_is_logged()
    {
        var logger = new CapturingLogger<InProcessEventBus>();
        await using var provider = BuildProvider(logger, s =>
        {
            s.AddScoped<IEventHandler<TestEvent>, ThrowingHandler>();
            s.AddScoped<IEventHandler<TestEvent>, SecondHandler>();
        });
        using var scope = provider.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        var evt = new TestEvent();
        await bus.PublishAsync(evt); // publisher must never observe the handler failure

        var log = scope.ServiceProvider.GetRequiredService<CallLog>();
        Assert.Contains("second:end", log.Entries);

        var error = Assert.Single(logger.Entries, e => e.Level == LogLevel.Error);
        Assert.IsType<InvalidOperationException>(error.Exception);
        Assert.Contains(nameof(ThrowingHandler), error.Message);
        Assert.Contains(evt.EventId.ToString(), error.Message);
    }

    [Fact]
    public async Task PublishAsync_resolves_handlers_from_the_ambient_scope()
    {
        var logger = new CapturingLogger<InProcessEventBus>();
        await using var provider = BuildProvider(logger, s =>
            s.AddScoped<IEventHandler<TestEvent>, SecondHandler>());

        using (var scopeA = provider.CreateScope())
        {
            var bus = scopeA.ServiceProvider.GetRequiredService<IEventBus>();
            await bus.PublishAsync(new TestEvent());
            Assert.Equal(2, scopeA.ServiceProvider.GetRequiredService<CallLog>().Count);
        }

        using var scopeB = provider.CreateScope();
        Assert.Equal(0, scopeB.ServiceProvider.GetRequiredService<CallLog>().Count);
    }

    [Fact]
    public async Task PublishAsync_awaits_handlers_sequentially_in_registration_order()
    {
        var logger = new CapturingLogger<InProcessEventBus>();
        await using var provider = BuildProvider(logger, s =>
        {
            s.AddScoped<IEventHandler<TestEvent>, FirstHandler>();
            s.AddScoped<IEventHandler<TestEvent>, SecondHandler>();
        });
        using var scope = provider.CreateScope();
        var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        await bus.PublishAsync(new TestEvent());

        var log = scope.ServiceProvider.GetRequiredService<CallLog>();
        Assert.Equal(["first:start", "first:end", "second:start", "second:end"], log.Entries);
    }
}
