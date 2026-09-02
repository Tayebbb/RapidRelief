using System.Runtime.CompilerServices;
using Serilog.AspNetCore;

namespace RapidRelief.Architecture.Tests;

/// <summary>
/// F9 risk 1 — SignalR clients pass their JWT as ?access_token=. Serilog must never write the
/// query string into the request log. Both halves matter: the default stays false AND nobody
/// re-enables it in Program.cs.
/// </summary>
public sealed class SerilogQueryLeakTests
{
    [Fact]
    public void Request_logging_does_not_include_the_query_string_by_default()
    {
        Assert.False(new RequestLoggingOptions().IncludeQueryInRequestPath);
    }

    [Fact]
    public void Program_never_enables_query_strings_in_the_request_path()
    {
        var source = File.ReadAllText(ProgramPath());

        Assert.DoesNotContain("IncludeQueryInRequestPath", source, StringComparison.Ordinal);
    }

    private static string ProgramPath([CallerFilePath] string thisFile = "")
        => Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(thisFile)!, "..", "..", "src", "RapidRelief.Api", "Program.cs"));
}
