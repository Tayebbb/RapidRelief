using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Infrastructure.Storage;

/// <summary>
/// Writes to {root}/{yyyy-MM}/{newGuid}{sanitized ext} — never trusts the client filename
/// (extension whitelist, original name discarded); rejects path traversal (blueprint B4).
/// Root from config FileStorage:Root, relative to ContentRoot unless absolute.
/// </summary>
public sealed class LocalDiskFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalDiskFileStorage(IConfiguration config, IHostEnvironment env)
        : this(ResolveRoot(config, env))
    {
    }

    public LocalDiskFileStorage(string root) => _root = root;

    private static string ResolveRoot(IConfiguration config, IHostEnvironment env)
    {
        var configured = config["FileStorage:Root"] ?? "App_Data/uploads";
        return Path.IsPathRooted(configured) ? configured : Path.Combine(env.ContentRootPath, configured);
    }

    public Task<StoredFile> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct = default)
        => SaveCoreAsync(content, fileName, contentType, ct);

    private async Task<StoredFile> SaveCoreAsync(Stream content, string fileName, string contentType, CancellationToken ct)
    {
        // Client filename is untrusted and discarded; only a whitelisted extension survives.
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            extension = string.Empty;
        }

        var monthFolder = DateTimeOffset.UtcNow.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        var storedName = $"{Guid.NewGuid():N}{extension}";
        var relativePath = $"{monthFolder}/{storedName}";

        var directory = Path.Combine(_root, monthFolder);
        Directory.CreateDirectory(directory);

        var fullPath = Path.Combine(directory, storedName);
        long size;
        await using (var file = File.Create(fullPath))
        {
            await content.CopyToAsync(file, ct);
            size = file.Length;
        }

        // Url = relative storage path; public serving is F2's decision (B4).
        return new StoredFile(relativePath, relativePath, size, contentType);
    }

    public Task<Stream?> OpenReadAsync(string path, CancellationToken ct = default)
    {
        var fullPath = ResolveSafe(path);
        Stream? stream = fullPath is not null && File.Exists(fullPath)
            ? File.OpenRead(fullPath)
            : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string path, CancellationToken ct = default)
    {
        var fullPath = ResolveSafe(path);
        if (fullPath is not null && File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
        return Task.CompletedTask;
    }

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".mp4", ".pdf",
    };

    /// <summary>Rejects null/empty, rooted, and traversal paths; confines resolution to the root.</summary>
    private string? ResolveSafe(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        var fullRoot = Path.GetFullPath(_root);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, path));
        return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) ? fullPath : null;
    }
}
