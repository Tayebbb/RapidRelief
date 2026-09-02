using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Features.Alerts.Domain;

public sealed class Alert
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AuthorGovernmentUserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public Severity Severity { get; set; }
    public DisasterType? DisasterType { get; set; }
    public string TargetArea { get; set; } = string.Empty;
    public double? RadiusKm { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
}
