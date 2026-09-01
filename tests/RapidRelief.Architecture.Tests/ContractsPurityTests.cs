using System.Reflection;
using NetArchTest.Rules;
using RapidRelief.Shared.Contracts.Eventing;

namespace RapidRelief.Architecture.Tests;

/// <summary>TEST PLAN items 2 and 3 — Shared stays a pure, dependency-free contracts assembly.</summary>
public sealed class ContractsPurityTests
{
    private static readonly Assembly SharedAssembly = typeof(IEvent).Assembly;
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;

    [Fact]
    public void Shared_types_depend_only_on_System_and_Shared()
    {
        var result = Types.InAssembly(SharedAssembly)
            .Should().OnlyHaveDependencyOn("System", "RapidRelief.Shared")
            .GetResult();

        Assert.True(result.IsSuccessful,
            "RapidRelief.Shared must be pure contracts. Violations: " +
            string.Join(", ", result.FailingTypes?.Select(t => t.FullName) ?? []));
    }

    [Fact]
    public void Shared_assembly_has_zero_nonframework_references()
    {
        var offenders = SharedAssembly.GetReferencedAssemblies()
            .Where(reference =>
                reference.Name is not ("netstandard" or "mscorlib") &&
                reference.Name?.StartsWith("System", StringComparison.Ordinal) != true)
            .Select(reference => reference.Name)
            .ToList();

        Assert.True(offenders.Count == 0,
            "RapidRelief.Shared must reference nothing beyond the base framework. Found: " +
            string.Join(", ", offenders));
    }

    [Fact]
    public void Every_concrete_event_resides_in_the_contracts_events_namespace()
    {
        foreach (var assembly in new[] { SharedAssembly, ApiAssembly })
        {
            var offenders = assembly.GetTypes()
                .Where(t => typeof(IEvent).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })
                .Where(t => t.Namespace != "RapidRelief.Shared.Contracts.Events")
                .Select(t => t.FullName)
                .ToList();

            Assert.True(offenders.Count == 0,
                $"Concrete IEvent implementors in {assembly.GetName().Name} must live in " +
                "RapidRelief.Shared.Contracts.Events. Found: " + string.Join(", ", offenders));
        }
    }
}
