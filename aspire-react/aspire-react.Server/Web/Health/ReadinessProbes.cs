using aspire_react.Server.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace aspire_react.Server.Web.Health;

/// <summary>[READY GĐ2] One dependency probe used by GET /api/v1/health/ready.</summary>
public interface IReadinessProbe
{
    /// <summary>Stable component name exposed in the response (e.g. "postgres", "redis").</summary>
    string Name { get; }

    /// <summary>True when the dependency is reachable; implementations MUST NOT throw.</summary>
    Task<bool> CheckAsync(CancellationToken cancellationToken);
}

/// <summary>Postgres readiness probe — real connection attempt with a hard timeout.</summary>
public sealed class PostgresReadinessProbe : IReadinessProbe
{
    private readonly AppDbContext _db;

    public PostgresReadinessProbe(AppDbContext db) => _db = db;

    public string Name => "postgres";

    public async Task<bool> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            return await _db.Database.CanConnectAsync(timeout.Token)
                .WaitAsync(TimeSpan.FromSeconds(6), cancellationToken);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>Redis readiness probe — PING with a hard timeout.</summary>
public sealed class RedisReadinessProbe : IReadinessProbe
{
    private readonly IConnectionMultiplexer _redis;

    public RedisReadinessProbe(IConnectionMultiplexer redis) => _redis = redis;

    public string Name => "redis";

    public async Task<bool> CheckAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _redis.GetDatabase().PingAsync().WaitAsync(TimeSpan.FromSeconds(4), cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
