using System.Text;
using RapidRelief.Api.Infrastructure.Storage;

namespace RapidRelief.Api.Tests.Storage;

public sealed class LocalDiskFileStorageTests : IDisposable
{
    private readonly string _root;
    private readonly LocalDiskFileStorage _storage;

    public LocalDiskFileStorageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"rr-storage-tests-{Guid.NewGuid():N}");
        _storage = new LocalDiskFileStorage(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static MemoryStream Payload(string text) => new(Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task SaveAsync_then_OpenReadAsync_roundtrips_content()
    {
        using var payload = Payload("hello rapid relief");

        var stored = await _storage.SaveAsync(payload, "photo.jpg", "image/jpeg");

        Assert.Equal(18, stored.SizeBytes);
        Assert.Equal("image/jpeg", stored.ContentType);
        Assert.EndsWith(".jpg", stored.Path);
        Assert.Matches(@"^\d{4}-\d{2}/", stored.Path);

        await using var readBack = await _storage.OpenReadAsync(stored.Path);
        Assert.NotNull(readBack);
        using var reader = new StreamReader(readBack!);
        Assert.Equal("hello rapid relief", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task SaveAsync_discards_client_filename_and_rejects_non_whitelisted_extension()
    {
        using var payload = Payload("malware");

        var stored = await _storage.SaveAsync(payload, "../../evil.exe", "application/octet-stream");

        Assert.DoesNotContain("..", stored.Path);
        Assert.DoesNotContain("evil", stored.Path);
        Assert.False(stored.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        Assert.False(Path.IsPathRooted(stored.Path));

        // The physical file must live inside the storage root.
        var physical = Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Single();
        Assert.StartsWith(Path.GetFullPath(_root), Path.GetFullPath(physical));
    }

    [Theory]
    [InlineData("photo.PNG", ".png")]
    [InlineData("clip.mp4", ".mp4")]
    [InlineData("scan.pdf", ".pdf")]
    [InlineData("pic.webp", ".webp")]
    public async Task SaveAsync_keeps_whitelisted_extensions_lowercased(string fileName, string expectedExtension)
    {
        using var payload = Payload("content");

        var stored = await _storage.SaveAsync(payload, fileName, "application/test");

        Assert.EndsWith(expectedExtension, stored.Path);
    }

    [Fact]
    public async Task OpenReadAsync_returns_null_for_missing_file()
    {
        var result = await _storage.OpenReadAsync("2026-09/00000000000000000000000000000000.jpg");

        Assert.Null(result);
    }

    [Theory]
    [InlineData("../outside.jpg")]
    [InlineData("2026-09/../../outside.jpg")]
    [InlineData(@"C:\windows\win.ini")]
    [InlineData("")]
    public async Task OpenReadAsync_returns_null_for_traversal_rooted_or_empty_paths(string hostile)
    {
        var result = await _storage.OpenReadAsync(hostile);

        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_removes_a_saved_file()
    {
        using var payload = Payload("temp");
        var stored = await _storage.SaveAsync(payload, "gone.png", "image/png");

        await _storage.DeleteAsync(stored.Path);

        Assert.Null(await _storage.OpenReadAsync(stored.Path));
    }

    [Fact]
    public async Task DeleteAsync_ignores_traversal_paths()
    {
        var marker = Path.Combine(Path.GetTempPath(), $"rr-delete-guard-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(marker, "do not delete");
        try
        {
            await _storage.DeleteAsync($"../{Path.GetFileName(marker)}");

            Assert.True(File.Exists(marker));
        }
        finally
        {
            File.Delete(marker);
        }
    }
}
