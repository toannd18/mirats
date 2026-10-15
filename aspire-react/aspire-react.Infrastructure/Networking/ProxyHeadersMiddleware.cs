using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace aspire_react.Server.Infrastructure.Networking;

/// <summary>
/// [REVERSE-PROXY] Pre-processes proxy headers BEFORE <c>UseForwardedHeaders</c>
/// runs, so the stock middleware resolves the real client IP + scheme with its
/// ForwardLimit semantics. Two repairs (both gated on trusted peer, fail-closed):
/// (1) folds a trusted CF-Connecting-IP into X-Forwarded-For; (2) adopts
/// X-Forwarded-Scheme=https when X-Forwarded-Proto is missing/http (NPM
/// overwrites X-Forwarded-Proto with its own downstream scheme).
///
/// Trust conditions for CF-Connecting-IP (ALL must hold — fail-closed):
/// 1. <see cref="ProxyOptions.TrustCloudflare"/> is true (explicit opt-in, config (c) only).
/// 2. The TCP peer must be a <see cref="ProxyOptions"/>-known proxy.
/// 3. The header value must parse as an IP; otherwise ignored.
/// In (a)/(b) condition 1 is false, so the header is unconditionally ignored —
/// a client forging it gains nothing. Condition 2 additionally ensures a direct
/// client cannot inject it even when condition 1 is on.
/// - Appended (never prepended/overwritten) and only when not already present,
///   preserving the left-to-right client→proxy order of X-Forwarded-For.
/// Runs first in the pipeline — must execute before UseForwardedHeaders.
/// </summary>
public sealed class ProxyHeadersMiddleware
{
    public const string CfConnectingIpHeader = "CF-Connecting-IP";
    public const string ForwardedSchemeHeader = "X-Forwarded-Scheme";

    private readonly RequestDelegate _next;
    private readonly ProxyOptions _options;

    public ProxyHeadersMiddleware(RequestDelegate next, IOptions<ProxyOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var knownPeer = _options.IsKnownProxy(context.Connection.RemoteIpAddress);

        // (1) CF-Connecting-IP fold (conditions in the class doc — fail-closed).
        if (_options.TrustCloudflare && knownPeer)
        {
            var raw = context.Request.Headers[CfConnectingIpHeader].FirstOrDefault()?.Trim();
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

        // (2) Scheme repair: NPM (and nginx generally) overwrites X-Forwarded-Proto
        // with its own downstream scheme (http), but NPM always sends
        // X-Forwarded-Scheme with the scheme IT terminated (https under Force SSL).
        // If our peer is trusted and X-Forwarded-Proto is missing/http while
        // X-Forwarded-Scheme is https, adopt https so the stock middleware sets
        // Request.Scheme correctly (QR URLs, cookie semantics). Same trust gate
        // as XFF — a direct client cannot forge either header.
        if (knownPeer)
        {
            var proto = context.Request.Headers["X-Forwarded-Proto"].ToString();
            var schemeHdr = context.Request.Headers[ForwardedSchemeHeader].ToString();
            if ((string.IsNullOrEmpty(proto) || proto.Equals("http", StringComparison.OrdinalIgnoreCase))
                && schemeHdr.Equals("https", StringComparison.OrdinalIgnoreCase))
            {
                context.Request.Headers["X-Forwarded-Proto"] = "https";
            }
        }

        await _next(context);
    }

    private static bool ContainsIp(string xff, string ip)
        => xff.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
              .Any(part => string.Equals(part, ip, StringComparison.OrdinalIgnoreCase));
}
