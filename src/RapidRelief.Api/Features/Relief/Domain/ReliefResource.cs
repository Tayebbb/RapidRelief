using RapidRelief.Shared.Contracts.Enums;

namespace RapidRelief.Api.Features.Relief.Domain;

public sealed class ReliefResource
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public ResourceType Category { get; set; } = ResourceType.Food;
    public double TotalQuantity { get; set; }
    public double AllocatedQuantity { get; set; }
    public string Unit { get; set; } = "Boxes";
    public string WarehouseLocation { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
