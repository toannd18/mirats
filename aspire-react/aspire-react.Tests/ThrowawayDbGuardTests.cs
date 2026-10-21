using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [DB-GUARD 2026-10-22] The safety gate must reject the dev database by name, even when the
/// environment flag says the DB is throwaway. These tests do not connect to Postgres.
/// </summary>
public class ThrowawayDbGuardTests
{
    private const string Host = "Host=127.0.0.1;Port=55467;Username=postgres;Password=test";

    [Theory]
    [InlineData("aspire_react")]
    [InlineData("aspire-react-db")]
    [InlineData("mirats")]
    [InlineData("mirats_dev")]
    [InlineData("postgres")]
    public void Guard_RejectsKnownDevDatabaseNames(string database)
    {
        var connectionString = $"{Host};Database={database}";
        Assert.False(ThrowawayDbGuard.IsThrowawayConnectionString(connectionString));
        Assert.Throws<InvalidOperationException>(() =>
            ThrowawayDbGuard.EnsureThrowawayConnectionString(connectionString));
    }

    [Theory]
    [InlineData("mirats_tmp_gate_20261022")]
    [InlineData("MIRATS_TMP_ABC")]
    public void Guard_AcceptsOnlyTempPrefixedDatabase(string database)
    {
        var connectionString = $"{Host};Database={database}";
        Assert.True(ThrowawayDbGuard.IsThrowawayConnectionString(connectionString));
        ThrowawayDbGuard.EnsureThrowawayConnectionString(connectionString);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-postgres-connection-string")]
    public void Guard_RejectsMissingOrMalformedConnectionString(string connectionString)
    {
        Assert.False(ThrowawayDbGuard.IsThrowawayConnectionString(connectionString));
        Assert.Throws<InvalidOperationException>(() =>
            ThrowawayDbGuard.EnsureThrowawayConnectionString(connectionString));
    }
}
