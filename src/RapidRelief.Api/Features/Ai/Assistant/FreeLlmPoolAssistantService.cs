using System.Diagnostics;
using RapidRelief.Api.Features.Ai.FreeLlmPool;

namespace RapidRelief.Api.Features.Ai.Assistant;

/// <summary>
/// D-050 provider chain: blank Ai:FreeLlmPool:BaseUrl (D-113 ops kill switch) or open breaker
/// → canned; blocked answers (HTTP 403 or finish_reason content_filter) and
/// empty-after-sanitize answers → canned WITHOUT counting against the shared breaker;
/// transport/parse failures → canned and counted. Never throws for answer failures. Logs
/// metadata only — never the question or the answer.
/// </summary>
internal sealed class FreeLlmPoolAssistantService : IAssistantService
{
    private readonly IFreeLlmPoolClient _client;
    private readonly AiCircuitBreaker _breaker;
    private readonly AssistantOptions _options;
    private readonly IConfiguration _config;
    private readonly ILogger<FreeLlmPoolAssistantService> _logger;

    public FreeLlmPoolAssistantService(
        IFreeLlmPoolClient client,
        AiCircuitBreaker breaker,
        AssistantOptions options,
        IConfiguration config,
        ILogger<FreeLlmPoolAssistantService> logger)
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

        var guardrailResult = AssistantGuardrails.EvaluateInput(ask.Question);
        if (guardrailResult.IsRefusal)
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "Assistant request refused by guardrails ({Reason}) after {LatencyMs} ms, question length {QuestionLength}",
                guardrailResult.Reason, stopwatch.ElapsedMilliseconds, ask.Question.Length);
            return new AssistantAnswer(guardrailResult.RefusalText, "Guardrail", Truncated: false,
                LatencyMs(stopwatch), TokensUsed: 0, FinishReason: "guardrail_refusal");
        }

        var baseUrl = _config["Ai:FreeLlmPool:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            // D-113: blank BaseUrl is the operator kill switch — never crashes and never counts
            // against the breaker (D-028 rule, now keyed off BaseUrl instead of ApiKey).
            return Canned(ask, stopwatch, "NoBaseUrl");
        }

        if (!_breaker.TryEnter())
        {
            return Canned(ask, stopwatch, "BreakerOpen");
        }

        try
        {
            var requestBody = AssistantPromptBuilder.Build(ask, _options, TextModel());
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

            var sanitized = AssistantGuardrails.EvaluateOutput(read.Text, _options.MaxAnswerLength);
            if (sanitized.Empty)
            {
                _breaker.AbandonProbe();
                return Canned(ask, stopwatch, "EmptyAfterSanitize", read.FinishReason);
            }

            stopwatch.Stop();
            _breaker.RecordSuccess();
            // Metadata only — never the question or the answer text (F8 carry-out).
            // Model = response.model, the actually routed model.
            _logger.LogInformation(
                "Assistant answered via FreeLlmPool: model {Model}, {LatencyMs} ms, {Tokens} tokens, finish {FinishReason}, question length {QuestionLength}",
                read.ModelName, stopwatch.ElapsedMilliseconds, read.TotalTokenCount, read.FinishReason, ask.Question.Length);

            return new AssistantAnswer(sanitized.Text, "FreeLlmPool", read.Truncated,
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
                "Assistant FreeLlmPool path failed ({ExceptionType}) after {LatencyMs} ms on model {Model} — answering canned guidance: {Reason}",
                ex.GetType().Name, stopwatch.ElapsedMilliseconds, TextModel(), ex.Message);
            return Canned(ask, stopwatch, "Exception");
        }
    }

    /// <summary>F16 always uses the D-113 text routing alias; empty config ⇒ "quality".</summary>
    private string TextModel()
        => _config["Ai:FreeLlmPool:TextModel"] is { Length: > 0 } configured ? configured : "quality";

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
