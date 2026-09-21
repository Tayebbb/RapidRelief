using RapidRelief.Client.Common.Map;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Client.Features.Relief;

/// <summary>Relief drop-offs onto the shared map.</summary>
public static class ReliefMapAdapters
{
    /// <summary>Everything still owed to someone — a delivered request is no longer an operation.</summary>
    public static bool IsOpen(this ReliefRequestDto request)
        => request.Status is ReliefStatus.Pending or ReliefStatus.Approved
            or ReliefStatus.Allocated or ReliefStatus.Dispatched;

    public static MapPlacemark ToPlacemark(this ReliefRequestDto request) => new(
        request.Id.ToString("N"),
        request.Location,
        $"{request.Type} × {request.Quantity}",
        Detail: request.RecipientCount > 0 ? $"{request.RecipientCount} people · {request.Status}" : request.Status.ToString(),
        Status: request.Status.ToString(),
        IsCritical: string.Equals(request.Urgency, "Critical", StringComparison.OrdinalIgnoreCase),
        Weight: Math.Max(1, request.RecipientCount));

    public static IEnumerable<MapPlacemark> ToPlacemarks(this IEnumerable<ReliefRequestDto> requests)
        => requests.Select(ToPlacemark);
}
