namespace RapidRelief.Api.Features.Ai.Gemini;

/// <summary>Thrown by the Gemini path when the live API cannot be used; the composite falls back.</summary>
public sealed class GeminiUnavailableException : Exception
{
    public GeminiUnavailableException(string message)
        : base(message)
    {
    }

    public GeminiUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
