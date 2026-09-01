using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace RapidRelief.Architecture.Tests;

/// <summary>
/// TEST PLAN item 4 — every DbContext lives in its owning feature's Data namespace
/// (PROJECT-CONTEXT §4.4). Passes trivially until SampleDbContext arrives in chunk 2.
/// </summary>
public sealed class DbContextOwnershipTests
{
    [Fact]
    public void Every_dbcontext_resides_in_a_feature_data_namespace()
    {
        var offenders = typeof(Program).Assembly.GetTypes()
            .Where(t => typeof(DbContext).IsAssignableFrom(t) && !t.IsAbstract)
            .Where(t => !Regex.IsMatch(t.Namespace ?? string.Empty, @"^RapidRelief\.Api\.Features\.[^.]+\.Data$"))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(offenders.Count == 0,
            "DbContexts must live in RapidRelief.Api.Features.<Feature>.Data. Found: " +
            string.Join(", ", offenders));
    }
}
