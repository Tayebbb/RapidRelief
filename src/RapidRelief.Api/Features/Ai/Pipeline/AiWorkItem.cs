using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Api.Features.Ai.Pipeline;

/// <summary>One queued analysis job (D-021).</summary>
public sealed record AiWorkItem(AiAnalysisRequest Request);
