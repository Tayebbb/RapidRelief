namespace RapidRelief.Api.Features.Shelters.Domain;

public sealed class ShelterSupply
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShelterId { get; set; }
    public string SupplyType { get; set; } = string.Empty;
    public double Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public DateTimeOffset LastReplenishedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
