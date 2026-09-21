namespace RapidRelief.Api.Features.Realtime.Domain;

public sealed class BroadcastAlert
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AuthorGovernmentUserId { get; set; }
    public string Headline { get; set; } = string.Empty;
    public string AlertBody { get; set; } = string.Empty;
    public string TargetArea { get; set; } = "All Dhaka";
    public double? RadiusKm { get; set; }
    public string Severity { get; set; } = "Critical";
    public DateTimeOffset ExpiresAtUtc { get; set; } = DateTimeOffset.UtcNow.AddHours(24);
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
