using aspire_react.Server.Application.ImportExport;
using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Authorization;
using aspire_react.Server.Infrastructure.Services;

namespace aspire_react.Server.Infrastructure;

/// <summary>
/// Registers infrastructure services (current-user, action-log, allocation services, company
/// scope, cache/accessor, lockout guard, local auth + WebAuthn). Extracted from Program.cs
/// (Task Q) — behavior and lifetimes unchanged. [AUTH Phase 5] The Keycloak admin API, JIT
/// provisioning and their HttpClient were removed when the migration completed.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Current User Service — reads local_user_id claim (stamped at local token issuance)
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

        // [FIX-J 2026-10-02 — N13+N19] PostgreSQL row locks (FOR UPDATE) kept in Infrastructure so
        // Application handlers stay provider-agnostic; no-op on InMemory (unit tests).
        services.AddScoped<aspire_react.Server.Domain.Interfaces.IRowLockService, RowLockService>();

        // [AUTH Phase 1] Local password authentication services (see AUTH_MIGRATION_PLAYBOOK
        // §11.1: contracts in Domain/Interfaces, framework-heavy implementations in
        // Infrastructure/Authentication).
        services.AddScoped<aspire_react.Server.Domain.Interfaces.IPasswordHasherService, Authentication.PasswordHasherService>();
        services.AddScoped<aspire_react.Server.Domain.Interfaces.ITokenService, Authentication.TokenService>();
        services.AddScoped<aspire_react.Server.Domain.Interfaces.IAuthAttemptService, Authentication.AuthAttemptService>();
        services.AddScoped<aspire_react.Server.Domain.Interfaces.IAuthCookieService, Authentication.AuthCookieService>();

        // [AUTH Phase 3] WebAuthn/passkey ceremonies — fido2-net-lib lives here ONLY (§11.1).
        services.AddScoped<aspire_react.Server.Domain.Interfaces.IWebAuthnService, Authentication.WebAuthn.Fido2Service>();

        return services;
    }
}
