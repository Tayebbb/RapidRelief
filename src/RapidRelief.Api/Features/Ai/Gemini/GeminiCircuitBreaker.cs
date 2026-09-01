namespace RapidRelief.Api.Features.Ai.Gemini;

/// <summary>
/// D-025 consecutive-failure breaker: 3 fails → open for 2 min → half-open single probe
/// (success closes, failure reopens). TimeProvider-injected, lock-guarded singleton.
/// Only Gemini-path failures may be recorded (blueprint risk 7).
/// </summary>
public sealed class GeminiCircuitBreaker
{
    private readonly TimeProvider _timeProvider;
    private readonly int _failureThreshold;
    private readonly TimeSpan _openDuration;
    private readonly object _gate = new();

    private int _consecutiveFailures;
    private DateTimeOffset? _openUntil;
    private bool _halfOpenProbeIssued;

    public GeminiCircuitBreaker(TimeProvider timeProvider, int failureThreshold, TimeSpan openDuration)
    {
        _timeProvider = timeProvider;
        _failureThreshold = failureThreshold;
        _openDuration = openDuration;
    }

    /// <summary>True when a Gemini attempt may proceed (closed, or the single half-open probe).</summary>
    public bool TryEnter()
    {
        lock (_gate)
        {
            if (_openUntil is null)
            {
                return true; // closed
            }
            if (_timeProvider.GetUtcNow() < _openUntil)
            {
                return false; // open
            }
            if (_halfOpenProbeIssued)
            {
                return false; // probe already in flight — everyone else keeps falling back
            }
            _halfOpenProbeIssued = true;
            return true;
        }
    }

    public void RecordSuccess()
    {
        lock (_gate)
        {
            _consecutiveFailures = 0;
            _openUntil = null;
            _halfOpenProbeIssued = false;
        }
    }

    public void RecordFailure()
    {
        lock (_gate)
        {
            if (_halfOpenProbeIssued)
            {
                // The half-open probe failed — reopen for a full window.
                _halfOpenProbeIssued = false;
                _consecutiveFailures = 0;
                _openUntil = _timeProvider.GetUtcNow() + _openDuration;
                return;
            }

            _consecutiveFailures++;
            if (_consecutiveFailures >= _failureThreshold)
            {
                _consecutiveFailures = 0;
                _openUntil = _timeProvider.GetUtcNow() + _openDuration;
            }
        }
    }
}
