using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace RapidRelief.Architecture.Tests;

/// <summary>
/// TEST PLAN item 4 — every DbContext lives in its owning feature's Data namespace
/// (PROJECT-CONTEXT §4.4). Non-vacuous since chunk 2 (SampleDbContext).
/// </summary>
public sealed class DbContextOwnershipTests
{
    private static List<Type> ConcreteDbContexts() =>
        typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(DbContext).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

    [Fact]
    public void At_least_one_dbcontext_exists_so_the_ownership_rule_is_not_vacuous()
    {
        Assert.NotEmpty(ConcreteDbContexts());
    }

    [Fact]
    public void Every_dbcontext_resides_in_a_feature_data_namespace()
    {
        var offenders = ConcreteDbContexts()
            .Where(t => !Regex.IsMatch(t.Namespace ?? string.Empty, @"^RapidRelief\.Api\.Features\.[^.]+\.Data$"))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(offenders.Count == 0,
            "DbContexts must live in RapidRelief.Api.Features.<Feature>.Data. Found: " +
            string.Join(", ", offenders));
    }
}
