using aspire_react.Server.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace aspire_react.Server.Infrastructure.Authorization;

/// <summary>
/// Registers permission-based authorization policies. Single source of truth: PermissionCatalog drives
/// registration so every permission code used by <c>[Authorize(Policy = "...")]</c> is guaranteed to be
/// registered (fixes e.g. customfields.delete which previously was used but never registered).
/// Extracted from Program.cs (Task Q) — behavior unchanged.
/// </summary>
public static class AuthorizationServiceCollectionExtensions
{
    public static IServiceCollection AddPermissionAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            foreach (var permission in PermissionCatalog.All)
            {
                options.AddPolicy(permission.Code, policy =>
                    policy.Requirements.Add(new PermissionRequirement(permission.Code)));
            }
        });

        services.AddScoped<IAuthorizationHandler, PermissionHandler>();

        // [DF-7b] Missing-permission 403s must carry an error_code (not an empty Forbid body).
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, PermissionAuthorizationResultHandler>();

        return services;
    }
}
