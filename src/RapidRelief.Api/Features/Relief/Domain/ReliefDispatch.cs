namespace RapidRelief.Api.Features.Relief.Domain;

public sealed class ReliefDispatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReliefRequestId { get; set; }
    public Guid ResourceId { get; set; }
    public double DispatchedQuantity { get; set; }
    public Guid DispatchedByUserId { get; set; }
    public string CarrierOrPartner { get; set; } = string.Empty;
    public string Status { get; set; } = "Preparing";
    public DateTimeOffset DispatchedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeliveredAtUtc { get; set; }

    public ReliefRequest? ReliefRequest { get; set; }
    public ReliefResource? Resource { get; set; }
}
