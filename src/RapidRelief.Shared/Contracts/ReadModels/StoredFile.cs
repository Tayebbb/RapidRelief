namespace RapidRelief.Shared.Contracts.ReadModels;

public sealed record StoredFile(string Path, string Url, long SizeBytes, string ContentType);
