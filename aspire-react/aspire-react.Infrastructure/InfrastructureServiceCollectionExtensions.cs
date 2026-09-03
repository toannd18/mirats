using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Authorization;
using aspire_react.Server.Infrastructure.Services;

namespace aspire_react.Server.Infrastructure;

/// <summary>
/// Registers infrastructure services (Keycloak admin API, JIT user provisioning, current-user,
/// action-log, allocation services, company scope, cache/accessor, lockout guard). Extracted from
/// Program.cs (Task Q) — behavior and lifetimes unchanged.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Keycloak Admin API Options (configured from the "Keycloak" config section)
        services.Configure<KeycloakOptions>(configuration.GetSection(KeycloakOptions.SectionName));

        // Named HttpClient for Keycloak Admin API
        services.AddHttpClient("KeycloakAdmin", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // Keycloak Service as Singleton (needs token caching across requests)
        services.AddSingleton<IKeycloakService, KeycloakService>();

        // JIT user provisioning — used by the Keycloak JWT OnTokenValidated handler (scoped AppDbContext).
        services.AddScoped<IJitUserProvisioningService, JitUserProvisioningService>();

        // Current User Service — reads local_user_id claim set by JIT provisioning
        services.AddScoped<ICurrentUserService, CurrentUserService>();

        // Action Logging Service (scoped — shares AppDbContext transaction)
        services.AddScoped<IActionLogService, ActionLogService>();

        // Component allocation/return/stock-in business rules (Bulk + Serial tracking)
        services.AddScoped<IComponentAllocationService, ComponentAllocationService>();

        // Consumable checkout business rules (stock check, user validation, company isolation, audit log)
        services.AddScoped<IConsumableAllocationService, ConsumableAllocationService>();

        // Excel (.xlsx) import — reference data + inventory sheets (T1–T4)
        services.AddScoped<IExcelImportService, ExcelImportService>();

        // Auto-generated Asset Tag (format + per-company/year counter) — Task ASSET-TAG-AUTO
        services.AddScoped<IAssetTagGenerator, AssetTagGenerator>();

        // Action-log company-visibility filter (shared by ReportsController + DashboardController, Task S1)
        services.AddScoped<IActionLogVisibilityService, ActionLogVisibilityService>();

        // Required by PermissionHandler + CompanyScopeService
        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.AddTransient<ICompanyScopeService, CompanyScopeService>();

        // Anti self-lockout guard for permission-management operations. Scoped because it uses AppDbContext.
        // [Giai đoạn 3] Interface registration added for Application handlers (Groups) — the concrete
        // registration stays for UsersController which still injects the concrete class.
        services.AddScoped<PermissionLockoutGuard>();
        services.AddScoped<aspire_react.Server.Domain.Interfaces.IPermissionLockoutGuard, PermissionLockoutGuard>();

        return services;
    }
}
