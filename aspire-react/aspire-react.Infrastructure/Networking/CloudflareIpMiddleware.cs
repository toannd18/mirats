using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace aspire_react.Server.Infrastructure.Networking;

/// <summary>
/// [REVERSE-PROXY] Folds a trusted CF-Connecting-IP into X-Forwarded-For BEFORE
/// <c>UseForwardedHeaders</c> runs, so the stock middleware resolves the real
/// client IP with its ForwardLimit semantics.
///
/// Rules (spoof-proof):
/// - The TCP peer must be a <see cref="ProxyOptions"/>-known proxy; otherwise
///   the header is ignored entirely (a direct client cannot inject it).
/// - The header value must parse as an IP; otherwise ignored.
/// - Appended (never prepended/overwritten) and only when not already present,
///   preserving the left-to-right client→proxy order of X-Forwarded-For.
/// Runs first in the pipeline — must execute before UseForwardedHeaders.
/// </summary>
public sealed class CloudflareIpMiddleware
{
    public const string HeaderName = "CF-Connecting-IP";

    private readonly RequestDelegate _next;
    private readonly ProxyOptions _options;

    public CloudflareIpMiddleware(RequestDelegate next, IOptions<ProxyOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (_options.IsKnownProxy(context.Connection.RemoteIpAddress))
        {
            var raw = context.Request.Headers[HeaderName].FirstOrDefault()?.Trim();
            if (IPAddress.TryParse(raw, out var cfIp))
            {
                cfIp = ProxyOptions.Normalize(cfIp);
                var xff = context.Request.Headers["X-Forwarded-For"].ToString();
                if (!ContainsIp(xff, cfIp.ToString()))
                {
                    context.Request.Headers["X-Forwarded-For"] =
                        string.IsNullOrEmpty(xff) ? cfIp.ToString() : xff + ", " + cfIp;
                }
            }
        }
        await _next(context);
    }

    private static bool ContainsIp(string xff, string ip)
        => xff.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
              .Any(part => string.Equals(part, ip, StringComparison.OrdinalIgnoreCase));
}
