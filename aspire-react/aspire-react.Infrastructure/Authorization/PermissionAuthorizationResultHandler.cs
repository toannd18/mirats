using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace aspire_react.Server.Infrastructure.Authorization;

/// <summary>
/// Returns a machine-readable JSON body for authorization (policy) failures instead of ASP.NET
/// Core's empty <c>403 Forbidden</c>. Business/company-scope 403s are already mapped by controllers;
/// this covers missing-permission denials raised by the authorization middleware.
/// </summary>
public sealed class PermissionAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                status = "error",
                message = "Bạn không có quyền thực hiện thao tác này.",
                error_code = "FORBIDDEN",
            });
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
