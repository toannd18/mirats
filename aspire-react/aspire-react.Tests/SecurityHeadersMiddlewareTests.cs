using aspire_react.Server.Web;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [READY GĐ0] The API must emit the same minimal security headers as the frontend Nginx.
/// HSTS is intentionally absent here — it belongs to the TLS terminator (NPM), see DEPLOYMENT §4.5.
/// </summary>
public class SecurityHeadersMiddlewareTests
{
    [Fact]
    public async Task AddsSecurityHeaders_WithoutHsts()
    {
        var context = new DefaultHttpContext();
        var middleware = new SecurityHeadersMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"].ToString());
        Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"].ToString());
        Assert.Equal("strict-origin-when-cross-origin", context.Response.Headers["Referrer-Policy"].ToString());
        Assert.Equal("geolocation=(), microphone=(), camera=()", context.Response.Headers["Permissions-Policy"].ToString());
        Assert.False(context.Response.Headers.ContainsKey("Strict-Transport-Security"));
    }

    [Fact]
    public async Task HeadersArePresent_WhenNextThrows()
    {
        var context = new DefaultHttpContext();
        var middleware = new SecurityHeadersMiddleware(_ => throw new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(context));

        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"].ToString());
        Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"].ToString());
    }
}
