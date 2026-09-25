namespace RapidRelief.Shared.Contracts.ReadModels;

public sealed record RescueTeamLiveDto(
    Guid Id,
    string TeamName,
    string Speciality,
    string Status,
    double Latitude,
    double Longitude,
    DateTimeOffset UpdatedAtUtc
);
