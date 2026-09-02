namespace RapidRelief.Api.Features.Ai;

/// <summary>
/// D-064: thrown when OpenRouter refuses the input on moderation grounds (HTTP 403).
/// Both services map it to canned/rule-based + <c>AbandonProbe()</c> WITHOUT counting a
/// breaker failure — three hostile reports must not disable AI globally for 2 minutes.
/// </summary>
public sealed class AiProviderBlockedException : Exception
{
    public AiProviderBlockedException(string message)
        : base(message)
    {
    }
}
