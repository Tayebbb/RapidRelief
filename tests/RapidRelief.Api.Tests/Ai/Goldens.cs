using System.Runtime.CompilerServices;

namespace RapidRelief.Api.Tests.Ai;

/// <summary>
/// Reads committed golden files from the source tree (compile-time path — valid locally and
/// in CI, which builds from the same checkout). Set UPDATE_GOLDENS=1 to rewrite them from
/// actual output after an intentional, blueprint-verified change.
/// </summary>
internal static class Goldens
{
    public static string Read(string fileName)
        => File.ReadAllText(PathFor(fileName)).TrimEnd('\r', '\n');

    public static void UpdateIfRequested(string fileName, string actual)
    {
        if (Environment.GetEnvironmentVariable("UPDATE_GOLDENS") != "1")
        {
            return;
        }

        // A silent self-heal in CI would let a regression rewrite its own pin.
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI")))
        {
            Assert.Fail("UPDATE_GOLDENS is refused in CI — goldens must be rewritten and reviewed locally.");
        }

        File.WriteAllText(PathFor(fileName), actual);
        Assert.Fail($"Golden '{fileName}' rewritten — rerun without UPDATE_GOLDENS to verify.");
    }

    private static string PathFor(string fileName, [CallerFilePath] string thisFile = "")
        => Path.Combine(Path.GetDirectoryName(thisFile)!, "Goldens", fileName);
}
