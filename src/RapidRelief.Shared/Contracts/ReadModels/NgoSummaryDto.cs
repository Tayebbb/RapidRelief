namespace RapidRelief.Shared.Contracts.ReadModels;

public sealed record NgoSummaryDto(Guid Id, string Name, IReadOnlyList<string> FocusAreas, string ContactEmail);
