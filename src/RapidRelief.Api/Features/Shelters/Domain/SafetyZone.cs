namespace RapidRelief.Api.Features.Shelters.Domain;

public sealed class SafetyZone
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string ZoneType { get; set; } = "SafeShelterZone";
    public string RiskLevel { get; set; } = "Safe";
    public string PolygonGeoJson { get; set; } = "{}";
    public bool IsActive { get; set; } = true;
    public string AdvisoryText { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
