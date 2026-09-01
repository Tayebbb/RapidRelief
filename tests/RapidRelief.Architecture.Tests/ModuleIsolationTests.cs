using System.Reflection;
using NetArchTest.Rules;

namespace RapidRelief.Architecture.Tests;

/// <summary>TEST PLAN items 1 and 5 — vertical-slice isolation (PROJECT-CONTEXT §4.1, §4.7).</summary>
public sealed class ModuleIsolationTests
{
    private const string FeaturesRoot = "RapidRelief.Api.Features";
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;

    private static IReadOnlyList<string> FeatureNames() =>
        ApiAssembly.GetTypes()
            .Select(t => t.Namespace)
            .Where(ns => ns is not null && ns.StartsWith(FeaturesRoot + ".", StringComparison.Ordinal))
            .Select(ns => ns![(FeaturesRoot.Length + 1)..].Split('.')[0])
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void Feature_namespaces_exist_so_isolation_rules_are_not_vacuous()
    {
        Assert.NotEmpty(FeatureNames());
    }

    [Fact]
    public void No_feature_depends_on_any_other_feature()
    {
        var features = FeatureNames();

        foreach (var source in features)
        {
            foreach (var target in features.Where(f => f != source))
            {
                var result = Types.InAssembly(ApiAssembly)
                    .That().ResideInNamespace($"{FeaturesRoot}.{source}")
                    .ShouldNot().HaveDependencyOnAny($"{FeaturesRoot}.{target}")
                    .GetResult();

                Assert.True(result.IsSuccessful,
                    $"Feature '{source}' must not depend on feature '{target}'. Violations: " +
                    string.Join(", ", result.FailingTypes?.Select(t => t.FullName) ?? []));
            }
        }
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_any_feature()
    {
        var result = Types.InAssembly(ApiAssembly)
            .That().ResideInNamespace("RapidRelief.Api.Infrastructure")
            .ShouldNot().HaveDependencyOnAny(FeaturesRoot)
            .GetResult();

        Assert.True(result.IsSuccessful,
            "Infrastructure must stay feature-agnostic (discovery is reflection-only). Violations: " +
            string.Join(", ", result.FailingTypes?.Select(t => t.FullName) ?? []));
    }
}
