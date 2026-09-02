using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace RapidRelief.Architecture.Tests;

/// <summary>
/// F16 post-review item 6 — the chat surface is the highest-risk render target in the app:
/// it prints model output that itself echoes user text. Model/user text must reach the DOM
/// only through Blazor's escaping @-interpolation, so no first-party client source may use
/// MarkupString, innerHTML, or a Markdown renderer. This converts a grep-proof into a guard.
/// </summary>
public sealed class ClientRenderSafetyTests
{
    private static readonly string[] ScannedExtensions = [".razor", ".cs", ".js"];

    private static readonly string[] ForbiddenTokens =
    [
        "MarkupString",
        "innerHTML",
        "outerHTML",
        "insertAdjacentHTML",
        "document.write",
        "Markdig",
        "Markdown.To",
        "ToHtml(",
    ];

    // Razor (@* *@), block (/* */) and line (//) comments — the rule is documented in prose
    // right next to the code it protects, and prose must not trip the guard.
    private static readonly Regex Comments = new(
        @"@\*.*?\*@|/\*.*?\*/|//[^\n]*", RegexOptions.Singleline, TimeSpan.FromSeconds(5));

    [Fact]
    public void The_scan_actually_sees_the_client_sources()
    {
        var files = ClientSourceFiles();

        Assert.True(files.Count >= 10, $"Expected to scan the client tree, found {files.Count} files.");
        Assert.Contains(files, f => f.EndsWith("Assistant.razor", StringComparison.Ordinal));
    }

    [Fact]
    public void No_first_party_client_source_renders_raw_markup()
    {
        var offenders = new List<string>();

        foreach (var file in ClientSourceFiles())
        {
            var code = Comments.Replace(File.ReadAllText(file), " ");
            offenders.AddRange(ForbiddenTokens
                .Where(token => code.Contains(token, StringComparison.Ordinal))
                .Select(token => $"{Path.GetFileName(file)} uses '{token}'"));
        }

        Assert.True(offenders.Count == 0,
            "Client sources must render text with @-interpolation only. Violations: " +
            string.Join(", ", offenders));
    }

    private static IReadOnlyList<string> ClientSourceFiles()
        => Directory.EnumerateFiles(ClientRoot(), "*.*", SearchOption.AllDirectories)
            .Where(path => ScannedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Where(path => !IsExcluded(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

    /// <summary>Build output and vendored libraries (Leaflet, Bootstrap) are not ours to police.</summary>
    private static bool IsExcluded(string path)
    {
        var relative = Path.GetRelativePath(ClientRoot(), path).Replace('\\', '/');
        return relative.StartsWith("bin/", StringComparison.Ordinal)
               || relative.StartsWith("obj/", StringComparison.Ordinal)
               || relative.StartsWith("wwwroot/lib/", StringComparison.Ordinal);
    }

    private static string ClientRoot([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(thisFile)!, "..", "..", "src", "RapidRelief.Client"));
}
