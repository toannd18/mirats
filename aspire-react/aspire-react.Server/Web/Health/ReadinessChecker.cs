using Microsoft.AspNetCore.Http;

namespace aspire_react.Server.Web.Health;

/// <summary>
/// [READY GĐ2] Sanitized readiness payload: ONLY aggregate status + per-component status. Never
/// includes connection strings, exception messages or other internal details.
/// </summary>
public sealed record ReadinessResponse(string Status, IReadOnlyDictionary<string, string> Checks)
{
    public const string HealthyStatus = "healthy";
    public const string UnhealthyStatus = "unhealthy";

    /// <summary>HTTP status for the readiness endpoint (200 when all probes are healthy, else 503).</summary>
    public int HttpStatusCode()
        => Status == HealthyStatus ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
}

/// <summary>
/// Runs every registered <see cref="IReadinessProbe"/> and aggregates their status. A throwing probe
/// is treated as unhealthy — the exception text is never surfaced in the response.
/// </summary>
public sealed class ReadinessChecker
{
    private readonly IReadOnlyList<IReadinessProbe> _probes;

    public ReadinessChecker(IEnumerable<IReadinessProbe> probes) => _probes = probes.ToList();

    public async Task<ReadinessResponse> CheckAsync(CancellationToken cancellationToken)
    {
        var checks = new Dictionary<string, string>(StringComparer.Ordinal);
        var allHealthy = true;

        foreach (var probe in _probes)
        {
            var healthy = false;
            try
            {
                healthy = await probe.CheckAsync(cancellationToken);
            }
            catch
            {
                // Defensive: a buggy probe must degrade readiness, not crash the endpoint.
                healthy = false;
            }

            checks[probe.Name] = healthy ? ReadinessResponse.HealthyStatus : ReadinessResponse.UnhealthyStatus;
            allHealthy &= healthy;
        }

        return new ReadinessResponse(
            allHealthy ? ReadinessResponse.HealthyStatus : ReadinessResponse.UnhealthyStatus,
            checks);
    }
}
