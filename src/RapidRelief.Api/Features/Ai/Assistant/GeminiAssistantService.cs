using System.Diagnostics;
using RapidRelief.Api.Features.Ai.Gemini;

namespace RapidRelief.Api.Features.Ai.Assistant;

/// <summary>
/// D-050 provider chain: empty key or open breaker → canned; blocked/empty answers → canned
/// WITHOUT counting against the shared breaker; transport/parse failures → canned and counted.
/// Never throws for answer failures. Logs metadata only — never the question or the answer.
/// </summary>
internal sealed class GeminiAssistantService : IAssistantService
{
    private readonly IGeminiClient _client;
    private readonly GeminiCircuitBreaker _breaker;
    private readonly AssistantOptions _options;
    private readonly IConfiguration _config;
    private readonly ILogger<GeminiAssistantService> _logger;

    public GeminiAssistantService(
        IGeminiClient client,
        GeminiCircuitBreaker breaker,
        AssistantOptions options,
        IConfiguration config,
        ILogger<GeminiAssistantService> logger)
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

        var apiKey = _config["Ai:Gemini:ApiKey"];
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
            var requestBody = AssistantPromptBuilder.Build(ask, _options);
            var responseBody = await _client.GenerateContentAsync(requestBody, isVision: false, ct);
            var read = AssistantResponseReader.Read(responseBody);

            if (read.Status == AssistantReadStatus.Invalid)
            {
                throw new GeminiUnavailableException($"Response rejected: {read.Reason}");
            }

            if (read.Status == AssistantReadStatus.Blocked)
            {
                // D-050: a block is a normal outcome, not an availability failure. Counting it
                // would let three hostile messages disable Gemini for every user for 2 minutes.
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
            _logger.LogInformation(
                "Assistant answered via Gemini: model {Model}, {LatencyMs} ms, {Tokens} tokens, finish {FinishReason}, question length {QuestionLength}",
                Model, stopwatch.ElapsedMilliseconds, read.TotalTokenCount, read.FinishReason, ask.Question.Length);

            return new AssistantAnswer(sanitized.Text, "Gemini", read.Truncated,
                LatencyMs(stopwatch), read.TotalTokenCount, read.FinishReason);
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
                "Assistant Gemini path failed ({ExceptionType}) after {LatencyMs} ms on model {Model} — answering canned guidance: {Reason}",
                ex.GetType().Name, stopwatch.ElapsedMilliseconds, Model, ex.Message);
            return Canned(ask, stopwatch, "Exception");
        }
    }

    private string Model => _config["Ai:Gemini:Model"] ?? "gemini-3.7-flash";

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
