using System.Text.Json;
using aspire_react.Server.Web.Health;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [READY GĐ2] Automated coverage for GET /api/v1/health/ready:
/// - 200 only when every probe is healthy;
/// - 503 when Postgres or Redis is down (or a probe throws);
/// - the serialized body contains ONLY status + per-component status (no connection string / error).
/// </summary>
public class ReadinessCheckerTests
{
    private sealed class FakeProbe : IReadinessProbe
    {
        private readonly Func<Task<bool>> _check;

        public FakeProbe(string name, Func<Task<bool>> check)
        {
            Name = name;
            _check = check;
        }

        public string Name { get; }

        public Task<bool> CheckAsync(CancellationToken cancellationToken) => _check();
    }

    private static IReadinessProbe Healthy(string name) => new FakeProbe(name, () => Task.FromResult(true));

    private static IReadinessProbe Unhealthy(string name) => new FakeProbe(name, () => Task.FromResult(false));

    private static ReadinessChecker Checker(params IReadinessProbe[] probes) => new(probes);

    [Fact]
    public async Task AllProbesHealthy_Returns200()
    {
        var result = await Checker(Healthy("postgres"), Healthy("redis")).CheckAsync(CancellationToken.None);

        Assert.Equal("healthy", result.Status);
        Assert.Equal(200, result.HttpStatusCode());
        Assert.Equal("healthy", result.Checks["postgres"]);
        Assert.Equal("healthy", result.Checks["redis"]);
    }

    [Fact]
    public async Task PostgresDown_Returns503_AndMarksOnlyPostgresUnhealthy()
    {
        var result = await Checker(Unhealthy("postgres"), Healthy("redis")).CheckAsync(CancellationToken.None);

        Assert.Equal("unhealthy", result.Status);
        Assert.Equal(503, result.HttpStatusCode());
        Assert.Equal("unhealthy", result.Checks["postgres"]);
        Assert.Equal("healthy", result.Checks["redis"]);
    }

    [Fact]
    public async Task RedisDown_Returns503_AndMarksOnlyRedisUnhealthy()
    {
        var result = await Checker(Healthy("postgres"), Unhealthy("redis")).CheckAsync(CancellationToken.None);

        Assert.Equal("unhealthy", result.Status);
        Assert.Equal(503, result.HttpStatusCode());
        Assert.Equal("healthy", result.Checks["postgres"]);
        Assert.Equal("unhealthy", result.Checks["redis"]);
    }

    [Fact]
    public async Task ProbeThrows_IsTreatedAsUnhealthy_WithoutLeakingException()
    {
        var throwing = new FakeProbe("postgres", () => throw new InvalidOperationException(
            "Host=secret-host;Password=super-secret"));

        var result = await Checker(throwing, Healthy("redis")).CheckAsync(CancellationToken.None);
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("unhealthy", result.Status);
        Assert.Equal(503, result.HttpStatusCode());
        Assert.Equal("unhealthy", result.Checks["postgres"]);
        Assert.DoesNotContain("secret-host", json);
        Assert.DoesNotContain("super-secret", json);
        Assert.DoesNotContain("InvalidOperationException", json);
    }

    [Fact]
    public void SerializedResponse_ContainsOnlyStatusAndChecks()
    {
        var result = new ReadinessResponse(
            ReadinessResponse.UnhealthyStatus,
            new Dictionary<string, string>
            {
                ["postgres"] = ReadinessResponse.UnhealthyStatus,
                ["redis"] = ReadinessResponse.HealthyStatus
            });

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal("{\"status\":\"unhealthy\",\"checks\":{\"postgres\":\"unhealthy\",\"redis\":\"healthy\"}}", json);
        Assert.DoesNotContain("Host=", json);
        Assert.DoesNotContain("Password", json);
        Assert.DoesNotContain("Npgsql", json);
        Assert.DoesNotContain("StackExchange", json);
        Assert.DoesNotContain("StackTrace", json);
        Assert.DoesNotContain("statusCode", json);
    }
}
