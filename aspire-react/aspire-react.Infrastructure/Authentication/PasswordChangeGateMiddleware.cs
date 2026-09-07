using aspire_react.Server.Domain.Interfaces;
using aspire_react.Server.Infrastructure.Authentication;
using Microsoft.AspNetCore.Http;

namespace aspire_react.Server.Infrastructure.Authentication;

/// <summary>
/// [AUTH Phase 1 gap fix — phát hiện qua Phase 2 live-verify] Middleware enforcing the
/// MustChangePassword flow designed in §4.4: a session carrying pwd_change=1 may ONLY touch
/// /api/v1/users/me (profile) and /api/v1/auth/password (the change itself) — EVERY other
/// endpoint gets 403 MUST_CHANGE_PASSWORD until the password is changed.
///
/// WHY a middleware and not OnTokenValidated context.Fail(): OnTokenValidated failures surface
/// as 401 via the challenge, but §4.4 requires 403 MUST_CHANGE_PASSWORD with a JSON body. A
/// dedicated middleware (placed after UseAuthentication, before UseAuthorization) has full
/// response control. The decision logic lives in PasswordChangeGate (unit-testable, exact-path
/// matching — /api/v1/users/me must never match /api/v1/users/{id}).
/// </summary>
public class PasswordChangeGateMiddleware
{
    private readonly RequestDelegate _next;

    public PasswordChangeGateMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && PasswordChangeGate.IsLimited(context.User)
            && !PasswordChangeGate.IsAllowedPath(context.Request.Path))
        {
            // §4.5-style semantics for the lockout path; the pwd_change gate is separate.
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                status = "error",
                message = "Bạn phải đổi mật khẩu trước khi sử dụng hệ thống.",
                error_code = "MUST_CHANGE_PASSWORD"
            });
            return;
        }

        await _next(context);
    }
}
