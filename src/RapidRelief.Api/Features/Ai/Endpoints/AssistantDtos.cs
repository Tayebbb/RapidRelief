using FluentValidation;
using RapidRelief.Api.Features.Ai.Assistant;

namespace RapidRelief.Api.Features.Ai.Endpoints;

// Feature-local wire records (D-019 precedent) — Shared/Contracts is untouched by F16.

/// <summary>
/// The client sends ONLY a session id and a message (D-048): history is server-owned, so a
/// caller can never forge a <c>role:"model"</c> turn and rewrite the guardrails.
/// </summary>
public sealed record AssistantMessageRequest(Guid? SessionId, string? Message, double? Latitude, double? Longitude);

public sealed record AssistantAnswerDto(string Text, string Provider, bool Truncated, DateTimeOffset CreatedAtUtc);

public sealed record AssistantMessageResponse(
    Guid? SessionId,
    AssistantAnswerDto Answer,
    bool Degraded,
    bool Persisted);

public sealed record AssistantMessageDto(Guid Id, string Role, string Text, string? Provider, DateTimeOffset CreatedAtUtc);

public sealed record AssistantHistoryResponse(Guid SessionId, IReadOnlyList<AssistantMessageDto> Messages);

/// <summary>Validated EXPLICITLY in the endpoint (B6 step 4 — never auto-validation).</summary>
public sealed class AssistantMessageRequestValidator : AbstractValidator<AssistantMessageRequest>
{
    public AssistantMessageRequestValidator(AssistantOptions options)
    {
        RuleFor(x => x.Message)
            .Cascade(CascadeMode.Stop)
            .NotNull().WithMessage("A message is required.")
            .Must(m => !string.IsNullOrWhiteSpace(m)).WithMessage("A message is required.")
            .Must(m => m!.Trim().Length <= options.MaxMessageLength)
                .WithMessage($"A message may be at most {options.MaxMessageLength} characters.");

        RuleFor(x => x.Latitude).InclusiveBetween(-90, 90).When(x => x.Latitude is not null);
        RuleFor(x => x.Longitude).InclusiveBetween(-180, 180).When(x => x.Longitude is not null);

        // Both-or-neither: half a coordinate pair selects no shelters and hides a client bug.
        RuleFor(x => x.Longitude)
            .NotNull().WithMessage("Longitude is required when latitude is supplied.")
            .When(x => x.Latitude is not null);
        RuleFor(x => x.Latitude)
            .NotNull().WithMessage("Latitude is required when longitude is supplied.")
            .When(x => x.Longitude is not null);
    }
}
