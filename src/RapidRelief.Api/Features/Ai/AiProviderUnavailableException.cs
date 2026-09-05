namespace RapidRelief.Api.Features.Ai;

/// <summary>
/// Thrown by the provider path when the live API cannot be used; the composite falls back
/// (counts against the breaker). <see cref="IsTransient"/> marks the failures worth one more
/// attempt — timeouts, network faults, 429 and 5xx. Everything else fails straight to fallback.
/// </summary>
public sealed class AiProviderUnavailableException : Exception
{
    public AiProviderUnavailableException(string message, bool isTransient = false)
        : base(message) => IsTransient = isTransient;

    public AiProviderUnavailableException(string message, Exception innerException, bool isTransient = false)
        : base(message, innerException) => IsTransient = isTransient;

    public bool IsTransient { get; }
}
