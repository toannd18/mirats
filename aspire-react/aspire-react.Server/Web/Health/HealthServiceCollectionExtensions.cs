namespace aspire_react.Server.Web.Health;

/// <summary>[READY GĐ2] DI registration for GET /api/v1/health/ready.</summary>
public static class HealthServiceCollectionExtensions
{
    public static IServiceCollection AddReadinessChecks(this IServiceCollection services)
    {
        // Postgres depends on the scoped AppDbContext; Redis is a singleton multiplexer.
        services.AddScoped<IReadinessProbe, PostgresReadinessProbe>();
        services.AddSingleton<IReadinessProbe, RedisReadinessProbe>();
        services.AddScoped<ReadinessChecker>();
        return services;
    }
}
