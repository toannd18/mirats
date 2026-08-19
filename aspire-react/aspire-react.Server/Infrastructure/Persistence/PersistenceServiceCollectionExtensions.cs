using HealthChecks.NpgSql;
using Microsoft.Extensions.Hosting;

namespace aspire_react.Server.Infrastructure.Persistence;

/// <summary>
/// Registers EF Core (<see cref="AppDbContext"/> via the Aspire Npgsql integration) plus the
/// PostgreSQL health check. Extracted from Program.cs (Task Q) — behavior/lifetime unchanged.
/// </summary>
public static class PersistenceServiceCollectionExtensions
{
    public static IHostApplicationBuilder AddPersistence(this IHostApplicationBuilder builder)
    {
        // EF Core + Aspire Npgsql client integration (connection named "aspire-react-db").
        builder.AddNpgsqlDbContext<AppDbContext>("aspire-react-db");

        // PostgreSQL health check (was part of the combined AddHealthChecks block in Program.cs).
        var dbConnectionString = builder.Configuration.GetConnectionString("aspire-react-db") ?? string.Empty;
        builder.Services.AddHealthChecks().AddNpgSql(
            connectionString: dbConnectionString,
            name: "postgresql",
            tags: ["db", "ready"]);

        return builder;
    }
}
