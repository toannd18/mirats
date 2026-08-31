using aspire_react.Server.Application.Common.Interfaces;
using HealthChecks.NpgSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
        // [Giai đoạn 0.1] DbContext giờ ở Infrastructure nhưng Migrations ở aspire-react.Server →
        // MigrationsAssembly phải chỉ định tường minh (default = assembly chứa DbContext), nếu không
        // Database.Migrate() lúc startup sẽ không thấy migration nào.
        builder.AddNpgsqlDbContext<AppDbContext>(
            "aspire-react-db",
            configureDbContextOptions: options =>
                options.UseNpgsql(o => o.MigrationsAssembly("aspire-react.Server")));

        // [Giai đoạn 0.1 — F2 phương án A] Handlers/validators inject IApplicationDbContext (Application
        // layer) — bound here to the SAME scoped AppDbContext instance, so resolution semantics are
        // identical to injecting AppDbContext directly.
        builder.Services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // PostgreSQL health check (was part of the combined AddHealthChecks block in Program.cs).
        var dbConnectionString = builder.Configuration.GetConnectionString("aspire-react-db") ?? string.Empty;
        builder.Services.AddHealthChecks().AddNpgSql(
            connectionString: dbConnectionString,
            name: "postgresql",
            tags: ["db", "ready"]);

        return builder;
    }
}
