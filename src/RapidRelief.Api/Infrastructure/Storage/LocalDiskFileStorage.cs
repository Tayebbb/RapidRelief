using RapidRelief.Shared.Contracts.ReadModels;
using RapidRelief.Shared.Contracts.Services;

namespace RapidRelief.Api.Infrastructure.Storage;

/// <summary>
/// Writes to {root}/{yyyy-MM}/{newGuid}{ext} — never trusts the client: non-whitelisted
/// extensions are rejected (ArgumentException), the stored ContentType is derived from the
/// extension map (caller claim ignored), and FileStorage:MaxSizeBytes (default 10 MiB) is
/// enforced while copying — on exceed the partial file is deleted. Rejects path traversal (B4).
/// Root from config FileStorage:Root, relative to ContentRoot unless absolute.
/// </summary>
public sealed class LocalDiskFileStorage : IFileStorage
{
    public const long DefaultMaxSizeBytes = 10_485_760;

    private readonly string _root;
    private readonly long _maxSizeBytes;

    public LocalDiskFileStorage(IConfiguration config, IHostEnvironment env)
        : this(ResolveRoot(config, env), config.GetValue("FileStorage:MaxSizeBytes", DefaultMaxSizeBytes))
    {
    }

    public LocalDiskFileStorage(string root, long maxSizeBytes = DefaultMaxSizeBytes)
    {
        _root = root;
        _maxSizeBytes = maxSizeBytes;
    }

    private static string ResolveRoot(IConfiguration config, IHostEnvironment env)
    {
        var configured = config["FileStorage:Root"] ?? "App_Data/uploads";
        return Path.IsPathRooted(configured) ? configured : Path.Combine(env.ContentRootPath, configured);
    }

    public Task<StoredFile> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct = default)
        => SaveCoreAsync(content, fileName, ct);

    private async Task<StoredFile> SaveCoreAsync(Stream content, string fileName, CancellationToken ct)
    {
        // Client filename is untrusted and discarded; a non-whitelisted extension is an error,
        // and the stored ContentType comes from the whitelist map — never the caller's claim.
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (!AllowedExtensionContentTypes.TryGetValue(extension, out var storedContentType))
        {
            throw new ArgumentException(
                $"File extension '{extension}' is not allowed. Allowed: {string.Join(", ", AllowedExtensionContentTypes.Keys)}.",
                nameof(fileName));
        }

        var monthFolder = DateTimeOffset.UtcNow.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        var storedName = $"{Guid.NewGuid():N}{extension}";
        var relativePath = $"{monthFolder}/{storedName}";

        var directory = Path.Combine(_root, monthFolder);
        Directory.CreateDirectory(directory);

        var fullPath = Path.Combine(directory, storedName);
        long size = 0;
        try
        {
            await using var file = File.Create(fullPath);
            var buffer = new byte[CopyBufferSize];
            int read;
            while ((read = await content.ReadAsync(buffer.AsMemory(), ct)) > 0)
            {
                size += read;
                if (size > _maxSizeBytes)
                {
                    throw new ArgumentException(
                        $"File exceeds the maximum allowed size of {_maxSizeBytes} bytes (FileStorage:MaxSizeBytes).",
                        nameof(content));
                }
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }
        catch
        {
            // Abort semantics: never leave a partial file behind.
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
            throw;
        }

        // Url = relative storage path; public serving is F2's decision (B4).
        return new StoredFile(relativePath, relativePath, size, storedContentType);
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

    private const int CopyBufferSize = 81_920;

    /// <summary>Whitelist doubles as the server-side ContentType source (never the caller's claim).</summary>
    private static readonly IReadOnlyDictionary<string, string> AllowedExtensionContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".webp"] = "image/webp",
            [".mp4"] = "video/mp4",
            [".pdf"] = "application/pdf",
        };

    /// <summary>Rejects null/empty, rooted, and traversal paths; confines resolution to the root.</summary>
    private string? ResolveSafe(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_root));
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, path));
        return fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? fullPath : null;
    }
}
