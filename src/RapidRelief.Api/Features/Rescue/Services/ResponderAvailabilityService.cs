using Microsoft.EntityFrameworkCore;
using RapidRelief.Api.Features.Rescue.Data;
using RapidRelief.Api.Features.Rescue.Domain;
using RapidRelief.Api.Infrastructure.Persistence;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Features.Rescue.Services;

/// <summary>
/// Real capacity read for consumers outside the rescue slice (F8 priority scoring, the assistant).
/// Never throws: an unreachable store answers <see cref="ResponderAvailabilityDto.Unknown"/>, which
/// the priority engine treats as "no information" rather than "no teams".
/// </summary>
public sealed class ResponderAvailabilityService(
    RescueDbContext db,
    DatabaseHealth health,
    ILogger<ResponderAvailabilityService> logger) : IResponderAvailabilityService
{
    public async Task<ResponderAvailabilityDto> GetAvailabilityAsync(
        GeoPoint? near = null, CancellationToken ct = default)
    {
        if (health.PostgresAvailable != true)
        {
            return ResponderAvailabilityDto.Unknown;
        }

        try
        {
            var teams = await db.Teams.AsNoTracking()
                .Select(t => new { t.Status, t.CurrentLatitude, t.CurrentLongitude })
                .ToListAsync(ct);

            var openMissions = await db.Missions.AsNoTracking()
                .CountAsync(m => m.Status != MissionStatus.Completed && m.Status != MissionStatus.Cancelled, ct);

            var available = teams.Where(t => t.Status == TeamStatus.Available).ToList();

            double? nearestKm = null;
            if (near is { } origin)
            {
                var distances = available
                    .Where(t => t.CurrentLatitude is not null && t.CurrentLongitude is not null)
                    .Select(t => TeamSuitabilityScorer.Haversine(origin,
                        new GeoPoint(t.CurrentLatitude!.Value, t.CurrentLongitude!.Value)))
                    .ToList();
                if (distances.Count > 0)
                {
                    nearestKm = Math.Round(distances.Min(), 2);
                }
            }

            return new ResponderAvailabilityDto(
                teams.Count,
                available.Count,
                teams.Count(t => t.Status == TeamStatus.Dispatched),
                openMissions,
                nearestKm);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Responder availability read failed — reporting unknown capacity");
            return ResponderAvailabilityDto.Unknown;
        }
    }
}
