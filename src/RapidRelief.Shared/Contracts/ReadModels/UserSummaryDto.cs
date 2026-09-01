namespace RapidRelief.Shared.Contracts.ReadModels;

public sealed record UserSummaryDto(Guid Id, string Email, string DisplayName,
    IReadOnlyList<string> Roles, bool IsLocked);
