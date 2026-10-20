using Microsoft.AspNetCore.Http;

namespace aspire_react.Server.Web;

/// <summary>
/// [READY GĐ0] Minimal security headers on every API response (same set as the frontend Nginx).
///
/// HSTS is deliberately NOT set here: TLS terminates at the reverse proxy (Nginx Proxy Manager),
/// which is the only hop that knows the public scheme. Setting HSTS on the plain-HTTP container
/// would be meaningless and could poison the wrong origin — see docs/DEPLOYMENT.md §4.5.
/// CSP is deferred to P1 (antd inline styles need a nonce/hash strategy first).
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";

        return _next(context);
    }
}
