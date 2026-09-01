namespace RapidRelief.Shared.Contracts.Common;

/// <summary>Success envelope only; errors are always RFC7807 ProblemDetails.</summary>
public sealed record ApiEnvelope<T>(T Data);
