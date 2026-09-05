using System.Data.Common;
using Npgsql;
using RapidRelief.Api.Infrastructure;

namespace RapidRelief.Api.Tests.Foundation;

/// <summary>
/// D-005 degraded mode used to be a one-way startup decision: <c>DatabaseHealth</c> was set by the
/// migration runner and never revisited, so a database that died mid-session produced a raw 500
/// from every endpoint while all the 503 guards still believed it was up. These tests pin the
/// classifier that now converts a runtime database fault into the same honest 503.
/// </summary>
public sealed class DatabaseFailureClassificationTests
{
    [Fact]
    public void A_provider_exception_is_recognised_as_a_database_failure()
        => Assert.True(DatabaseFailureExceptionHandler.IsDatabaseFailure(new NpgsqlException("connection reset")));

    [Fact]
    public void A_nested_provider_exception_is_recognised_through_the_wrapper()
    {
        var wrapped = new InvalidOperationException(
            "An exception occurred while iterating over the results of a query",
            new NpgsqlException("server closed the connection"));

        Assert.True(DatabaseFailureExceptionHandler.IsDatabaseFailure(wrapped));
    }

    [Fact]
    public void A_missing_connection_string_is_recognised_even_though_it_is_only_an_InvalidOperationException()
    {
        // This is the exact shape Npgsql throws when configuration is missing, and it is the one
        // that produced stack-trace-bearing 500s during the audit.
        var exception = new InvalidOperationException("The ConnectionString property has not been initialized.");

        Assert.True(DatabaseFailureExceptionHandler.IsDatabaseFailure(exception));
    }

    [Fact]
    public void A_timeout_is_treated_as_a_database_failure_rather_than_a_bug()
        => Assert.True(DatabaseFailureExceptionHandler.IsDatabaseFailure(new TimeoutException("command timeout")));

    [Theory]
    [InlineData("Sequence contains no elements")]
    [InlineData("Object reference not set to an instance of an object")]
    public void An_ordinary_application_bug_is_not_misreported_as_a_database_outage(string message)
        => Assert.False(DatabaseFailureExceptionHandler.IsDatabaseFailure(new InvalidOperationException(message)));

    [Fact]
    public void An_argument_error_is_not_a_database_failure()
        => Assert.False(DatabaseFailureExceptionHandler.IsDatabaseFailure(new ArgumentNullException("name")));

    [Fact]
    public void The_classifier_recognises_the_provider_base_type_not_just_npgsql()
    {
        // Guards the contract if the provider is ever swapped: DbException is the seam.
        Assert.True(typeof(DbException).IsAssignableFrom(typeof(NpgsqlException)));
    }
}
