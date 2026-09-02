using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Client.Features.Alerts;

public sealed record CreateAlertRequest(
    string Title,
    string Body,
    Severity Severity,
    DisasterType? DisasterType,
    string TargetArea,
    double? RadiusKm,
    DateTimeOffset ExpiresAtUtc);

public sealed record AlertDto(
    Guid Id,
    string Title,
    string Body,
    Severity Severity,
    DisasterType? DisasterType,
    string TargetArea,
    double? RadiusKm,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? RevokedAtUtc);

public interface IAlertsApi
{
    Task<ApiEnvelope<PagedResult<AlertDto>>?> GetAsync(int page = 1, int pageSize = 20, CancellationToken ct = default);
    Task<IReadOnlyList<AlertDto>> GetActiveAsync(CancellationToken ct = default);
    Task<HttpResponseMessage> CreateAsync(CreateAlertRequest request, CancellationToken ct = default);
    Task<HttpResponseMessage> RevokeAsync(Guid id, CancellationToken ct = default);
}
