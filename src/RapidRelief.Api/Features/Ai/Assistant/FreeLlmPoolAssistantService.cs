using System.Diagnostics;
using RapidRelief.Api.Features.Ai.OpenRouter;

namespace RapidRelief.Api.Features.Ai.Assistant;

/// <summary>
/// D-050 provider chain: empty key or open breaker → canned; blocked answers (HTTP 403 or
/// finish_reason content_filter) and empty-after-sanitize answers → canned WITHOUT counting
/// against the shared breaker; transport/parse failures → canned and counted. Never throws
/// for answer failures. Logs metadata only — never the question or the answer.
/// </summary>
internal sealed class OpenRouterAssistantService : IAssistantService
{
    private readonly IOpenRouterClient _client;
    private readonly AiCircuitBreaker _breaker;
    private readonly AssistantOptions _options;
    private readonly IConfiguration _config;
    private readonly ILogger<OpenRouterAssistantService> _logger;

    public OpenRouterAssistantService(
        IOpenRouterClient client,
        AiCircuitBreaker breaker,
        AssistantOptions options,
        IConfiguration config,
        ILogger<OpenRouterAssistantService> logger)
    {
        _client = client;
        _breaker = breaker;
        _options = options;
        _config = config;
        _logger = logger;
    }

    public async Task<AssistantAnswer> AskAsync(AssistantAsk ask, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();

        var apiKey = _config["Ai:OpenRouter:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // A missing key never crashes and never counts against the breaker (D-028 rule).
            return Canned(ask, stopwatch, "NoApiKey");
        }

        if (!_breaker.TryEnter())
        {
            return Canned(ask, stopwatch, "BreakerOpen");
        }

        try
        {
            var requestBody = AssistantPromptBuilder.Build(ask, _options, TextModels());
            var responseBody = await _client.SendAsync(requestBody, isVision: false, ct);
            var read = AssistantResponseReader.Read(responseBody);

            if (read.Status == AssistantReadStatus.Invalid)
            {
                throw new AiProviderUnavailableException($"Response rejected: {read.Reason}");
            }

            if (read.Status == AssistantReadStatus.Blocked)
            {
                // D-050/D-064: a block is a normal outcome, not an availability failure. Counting
                // it would let three hostile messages disable AI for every user for 2 minutes.
                _breaker.AbandonProbe();
                return Canned(ask, stopwatch, read.Reason ?? "Blocked", read.FinishReason);
            }

            var sanitized = AssistantSanitizer.Clean(read.Text, _options.MaxAnswerLength);
            if (sanitized.Empty)
            {
                _breaker.AbandonProbe();
                return Canned(ask, stopwatch, "EmptyAfterSanitize", read.FinishReason);
            }

            stopwatch.Stop();
            _breaker.RecordSuccess();
            // Metadata only — never the question or the answer text (F8 carry-out).
            // Model = response.model, the actually routed model (D-061).
            _logger.LogInformation(
                "Assistant answered via OpenRouter: model {Model}, {LatencyMs} ms, {Tokens} tokens, finish {FinishReason}, question length {QuestionLength}",
                read.ModelName, stopwatch.ElapsedMilliseconds, read.TotalTokenCount, read.FinishReason, ask.Question.Length);

            return new AssistantAnswer(sanitized.Text, "OpenRouter", read.Truncated,
                LatencyMs(stopwatch), read.TotalTokenCount, read.FinishReason);
        }
        catch (AiProviderBlockedException)
        {
            // D-064: HTTP 403 = input moderation — canned outcome, no breaker count, probe freed.
            _breaker.AbandonProbe();
            return Canned(ask, stopwatch, "ProviderBlocked");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Caller cancellation would otherwise strand a half-open probe forever.
            _breaker.AbandonProbe();
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _breaker.RecordFailure();
            _logger.LogWarning(
                "Assistant OpenRouter path failed ({ExceptionType}) after {LatencyMs} ms on model {Model} — answering canned guidance: {Reason}",
                ex.GetType().Name, stopwatch.ElapsedMilliseconds, TextModels()[0], ex.Message);
            return Canned(ask, stopwatch, "Exception");
        }
    }

    /// <summary>F16 always uses the D-061 text pair; empty fallback ⇒ single-element array.</summary>
    private IReadOnlyList<string> TextModels()
    {
        var primary = _config["Ai:OpenRouter:TextModel"] ?? "z-ai/glm-5.2:free";
        var fallback = _config["Ai:OpenRouter:TextFallbackModel"];
        return string.IsNullOrWhiteSpace(fallback) ? [primary] : [primary, fallback];
    }

    private AssistantAnswer Canned(AssistantAsk ask, Stopwatch stopwatch, string reason, string? finishReason = null)
    {
        stopwatch.Stop();
        _logger.LogInformation(
            "Assistant answered from canned guidance ({Reason}, finish {FinishReason}) after {LatencyMs} ms, question length {QuestionLength}",
            reason, finishReason, stopwatch.ElapsedMilliseconds, ask.Question.Length);
        return CannedSafetyResponses.For(ask.Question, LatencyMs(stopwatch));
    }

    private static int LatencyMs(Stopwatch stopwatch)
        => (int)Math.Min(stopwatch.ElapsedMilliseconds, int.MaxValue);
}
