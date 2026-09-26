using System.Text.RegularExpressions;

namespace RapidRelief.Api.Features.Ai.Assistant;

/// <summary>Result of <see cref="AssistantGuardrails.EvaluateInput"/>.</summary>
internal readonly record struct InputGuardrailResult(bool IsRefusal, string RefusalText, string Reason);

/// <summary>
/// Enforces input and output guardrails for the RapidRelief Emergency Assistant:
/// 1. Input Guardrail: Detects and blocks prompt injection/jailbreaks, harmful/dangerous requests,
///    and off-topic queries outside emergency disaster safety & preparedness.
/// 2. Output Guardrail: Audits generated model text to prevent system instruction leaks or safety violations.
/// </summary>
internal static partial class AssistantGuardrails
{
    public const string RefusalMessage =
        "I am the RapidRelief Emergency Assistant. I can only assist with emergency disaster safety, shelter, first aid, and preparedness guidance in Bangladesh. For life-threatening emergencies, please call 999 immediately.";

    private static readonly string[] DisasterKeywords =
    [
        "flood", "water", "fire", "cyclone", "earthquake", "landslide", "collapse",
        "shelter", "rescue", "drowning", "evacuate", "evacuation", "first aid", "storm",
        "rain", "river", "boat", "relief", "hospital", "doctor", "injury", "burn", "sos",
        "emergency", "999", "help", "danger", "safe", "safety", "hazard", "disaster",
        "bangladesh", "dhaka", "sylhet", "chittagong", "noakhali", "feni", "comilla",
        "food", "clean water", "power outage", "electricity", "gas leak", "heatwave"
    ];

    public static InputGuardrailResult EvaluateInput(string? question)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return new InputGuardrailResult(false, string.Empty, string.Empty);
        }

        var text = question.Trim();

        // 1. Prompt Injection / Jailbreak Detection
        if (PromptInjectionRegex().IsMatch(text))
        {
            return new InputGuardrailResult(true, RefusalMessage, "PromptInjectionAttempt");
        }

        // 2. Harmful / Illegal Request Detection
        if (HarmfulContentRegex().IsMatch(text))
        {
            return new InputGuardrailResult(
                true,
                "For life-threatening emergencies or mental health crises, please call 999 or your local emergency hotline immediately. I cannot assist with harmful or dangerous requests.",
                "HarmfulContentDetected");
        }

        // 3. Off-Topic Query Detection (Code, Creative Writing, Financial, Math, Trivia)
        if (OffTopicRegex().IsMatch(text) && !ContainsDisasterKeyword(text))
        {
            return new InputGuardrailResult(true, RefusalMessage, "OffTopicQuery");
        }

        return new InputGuardrailResult(false, string.Empty, string.Empty);
    }

    public static SanitizedAnswer EvaluateOutput(string? rawOutput, int maxLength)
    {
        var sanitized = AssistantSanitizer.Clean(rawOutput, maxLength);
        if (sanitized.Empty)
        {
            return sanitized;
        }

        // Check for system instruction prompt leakage
        if (sanitized.Text.Contains("You are the RapidRelief Emergency Assistant", StringComparison.OrdinalIgnoreCase) ||
            sanitized.Text.Contains("systemInstruction", StringComparison.OrdinalIgnoreCase))
        {
            return new SanitizedAnswer(
                "If you are facing an emergency, move to a safe location immediately and call 999 for emergency assistance.",
                Empty: false);
        }

        return sanitized;
    }

    private static bool ContainsDisasterKeyword(string text)
    {
        var lower = text.ToLowerInvariant();
        return DisasterKeywords.Any(k => lower.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(@"(?i)\b(?:ignore|disregard|forget|override)\s+(?:all\s+)?(?:previous\s+)?(?:instructions|rules|prompts|system)|reveal\s+(?:your\s+)?(?:system\s+)?(?:prompt|rules|instructions)|you\s+are\s+now\s+(?:dan|jailbroken|unrestricted)|bypass\s+(?:safety|guardrails)|print\s+(?:system\s+)?instruction",
        RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PromptInjectionRegex();

    [GeneratedRegex(@"(?i)\b(?:how\s+to\s+make\s+a\s+bomb|build\s+explosive|synthesis\s+cyanide|commit\s+suicide|how\s+to\s+kill\s+myself|meth\s+recipe|hack\s+into|steal\s+credit\s+card)\b",
        RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HarmfulContentRegex();

    [GeneratedRegex(@"(?i)\b(?:write\s+(?:a\s+)?(?:python|javascript|c#|java|html|css|react|sql|code|script|poem|story|song|essay)|solve\s+math|calculate\s+\d+|crypto\s+tips|buy\s+stock|who\s+should\s+i\s+vote\s+for|capital\s+of\s+\w+)\b",
        RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex OffTopicRegex();
}
