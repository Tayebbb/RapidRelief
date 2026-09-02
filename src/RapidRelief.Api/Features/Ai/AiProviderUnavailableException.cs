namespace RapidRelief.Api.Features.Ai;

/// <summary>Thrown by the provider path when the live API cannot be used; the composite falls back (counts against the breaker).</summary>
public sealed class AiProviderUnavailableException : Exception
{
    public AiProviderUnavailableException(string message)
        : base(message)
    {
    }

    public AiProviderUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
