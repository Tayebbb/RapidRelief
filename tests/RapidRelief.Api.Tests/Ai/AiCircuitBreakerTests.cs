using RapidRelief.Api.Features.Ai;

namespace RapidRelief.Api.Tests.Ai;

/// <summary>
/// F8 blueprint TEST PLAN item 4 / D-025: 3 consecutive fails → open 2 min → half-open
/// single probe; probe success closes, probe failure reopens. Clock is fully pinned.
/// </summary>
public sealed class AiCircuitBreakerTests
{
    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private static readonly TimeSpan OpenDuration = TimeSpan.FromMinutes(2);

    private static AiCircuitBreaker Create(TestClock clock) => new(clock, 3, OpenDuration);

    [Fact]
    public void New_breaker_allows_requests()
    {
        var breaker = Create(new TestClock());

        Assert.True(breaker.TryEnter());
    }

    [Fact]
    public void Two_failures_do_not_open_the_breaker()
    {
        var breaker = Create(new TestClock());

        breaker.RecordFailure();
        breaker.RecordFailure();

        Assert.True(breaker.TryEnter());
    }

    [Fact]
    public void Three_consecutive_failures_open_the_breaker()
    {
        var breaker = Create(new TestClock());

        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.RecordFailure();

        Assert.False(breaker.TryEnter());
    }

    [Fact]
    public void Success_resets_the_consecutive_counter()
    {
        var breaker = Create(new TestClock());

        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.RecordSuccess();
        breaker.RecordFailure();
        breaker.RecordFailure();

        Assert.True(breaker.TryEnter()); // never reached 3 in a row
    }

    [Fact]
    public void Breaker_stays_open_for_the_whole_window()
    {
        var clock = new TestClock();
        var breaker = Create(clock);
        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.RecordFailure();

        clock.Advance(OpenDuration - TimeSpan.FromSeconds(1));

        Assert.False(breaker.TryEnter());
    }

    [Fact]
    public void After_the_open_window_exactly_one_half_open_probe_is_allowed()
    {
        var clock = new TestClock();
        var breaker = Create(clock);
        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.RecordFailure();

        clock.Advance(OpenDuration);

        Assert.True(breaker.TryEnter());  // the single probe
        Assert.False(breaker.TryEnter()); // everyone else keeps falling back
    }

    [Fact]
    public void Half_open_probe_success_closes_the_breaker()
    {
        var clock = new TestClock();
        var breaker = Create(clock);
        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.RecordFailure();
        clock.Advance(OpenDuration);
        Assert.True(breaker.TryEnter());

        breaker.RecordSuccess();

        Assert.True(breaker.TryEnter());
        Assert.True(breaker.TryEnter()); // fully closed, not another lone probe
    }

    [Fact]
    public void Half_open_probe_failure_reopens_for_a_full_window()
    {
        var clock = new TestClock();
        var breaker = Create(clock);
        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.RecordFailure();
        clock.Advance(OpenDuration);
        Assert.True(breaker.TryEnter());

        breaker.RecordFailure(); // probe failed

        Assert.False(breaker.TryEnter());
        clock.Advance(OpenDuration - TimeSpan.FromSeconds(1));
        Assert.False(breaker.TryEnter());
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(breaker.TryEnter()); // next probe
    }

    [Fact]
    public void Abandoned_half_open_probe_frees_the_slot_for_a_new_probe()
    {
        var clock = new TestClock();
        var breaker = Create(clock);
        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.RecordFailure();
        clock.Advance(OpenDuration);
        Assert.True(breaker.TryEnter()); // the single probe

        breaker.AbandonProbe(); // holder cancelled — will never Record success/failure

        Assert.True(breaker.TryEnter());  // a fresh probe may proceed — breaker not wedged
        Assert.False(breaker.TryEnter()); // still exactly one probe at a time
    }

    [Fact]
    public void Abandon_without_a_probe_in_flight_is_a_harmless_no_op()
    {
        var breaker = Create(new TestClock());

        breaker.AbandonProbe();

        Assert.True(breaker.TryEnter()); // closed breaker unaffected
        breaker.RecordFailure();
        breaker.RecordFailure();
        Assert.True(breaker.TryEnter()); // counter untouched — still below threshold
    }
}
