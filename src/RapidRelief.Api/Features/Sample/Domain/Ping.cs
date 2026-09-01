namespace RapidRelief.Api.Features.Sample.Domain;

/// <summary>Sample-slice entity. Provider-portable types ONLY (Guid/string/DateTimeOffset) — see F0-blueprint risk 4.</summary>
public sealed class Ping
{
    public Guid Id { get; set; }

    public required string Message { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; }
}
