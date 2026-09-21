namespace RapidRelief.Api.Features.Incidents.Domain;

public sealed class IncidentMedia
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid IncidentId { get; set; }
    public string FileUrl { get; set; } = string.Empty;
    public string MediaType { get; set; } = "image/jpeg";
    public long FileSizeBytes { get; set; }
    public string Caption { get; set; } = string.Empty;
    public DateTimeOffset UploadedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public IncidentReport? Incident { get; set; }
}
