using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Features.Relief.Domain;

public sealed class ReliefRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequesterId { get; set; }
    public Guid? IncidentId { get; set; }
    public string ReliefType { get; set; } = "Food";
    public string UrgencyLevel { get; set; } = "High";
    public int QuantityRequested { get; set; } = 1;
    public int RecipientCount { get; set; } = 1;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string DeliveryAddress { get; set; } = string.Empty;
    public ReliefStatus Status { get; set; } = ReliefStatus.Pending;
    public string Notes { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<ReliefDispatch> Dispatches { get; set; } = new List<ReliefDispatch>();
}
