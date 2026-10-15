using System.Linq;
using System.Net;
using aspire_react.Server.Infrastructure.Networking;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace aspire_react.Tests;

/// <summary>
/// [REVERSE-PROXY] Tests for trusted-proxy header handling (no TestHost needed —
/// both middlewares are invoked directly on DefaultHttpContext).
/// Contract: headers from a NON-proxy peer are ignored (spoof-proof); a trusted
/// peer's X-Forwarded-For / CF-Connecting-IP resolve the real client IP; scheme
/// follows X-Forwarded-Proto.
/// </summary>
public class ProxyHeadersTests
{
    private static readonly IPAddress Frontend = IPAddress.Parse("172.22.0.5");
    private static readonly IPAddress Npm = IPAddress.Parse("10.9.0.5");
    private static readonly IPAddress Client = IPAddress.Parse("203.0.113.7");
    private static readonly IPAddress Stranger = IPAddress.Parse("198.51.100.9");

    private static ProxyOptions OptionsFor(params string[] known)
        => new() { KnownNetworks = string.Join(",", known), ForwardLimit = 3 };

    private static DefaultHttpContext Context(IPAddress peer)
    {
        var ctx = new DefaultHttpContext();
        ctx.Connection.RemoteIpAddress = peer;
        return ctx;
    }

    private static async Task RunPairAsync(DefaultHttpContext ctx, ProxyOptions options)
    {
        var cf = new CloudflareIpMiddleware(_ => Task.CompletedTask, Options.Create(options));
        await cf.InvokeAsync(ctx);
        var fw = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = options.ForwardLimit,
        };
        // Same entry parsing as NetworkingServiceCollectionExtensions (HttpOverrides
        // ships its own IPNetwork struct — framework types here, System.Net in ProxyOptions),
        // including the IPv4-mapped /108+ counterparts for Kestrel's ::ffff:a.b.c.d peers
        // and comma-splitting (env binds CSV as one scalar).
        foreach (var entry in ProxyOptions.SplitEntries(options.KnownNetworks))
        {
            if (entry.Contains('/'))
            {
                var parts = entry.Split('/');
                if (parts.Length == 2
                    && System.Net.IPAddress.TryParse(parts[0].Trim(), out var addr)
                    && int.TryParse(parts[1].Trim(), out var prefix))
                {
                    fw.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(addr, prefix));
                    if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        var mapped = new System.Net.IPAddress(
                            new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0xFF }
                            .Concat(addr.GetAddressBytes()).ToArray());
                        fw.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(mapped, prefix + 96));
                    }
                }
            }
            else if (System.Net.IPAddress.TryParse(entry, out var ip))
            {
                fw.KnownProxies.Add(ip);
            }
        }
        var fwd = new ForwardedHeadersMiddleware(
            _ => Task.CompletedTask,
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            Options.Create(fw));
        await fwd.Invoke(ctx);
    }

    [Fact]
    public async Task ConfigA_DirectViaFrontendContainer_ResolvesBrowserIp()
    {
        // (a) Browser → frontend nginx → backend. Peer = frontend container (known),
        // XFF = browser IP (nginx appended). Backend must see the browser, not the container.
        var ctx = Context(Frontend);
        ctx.Request.Headers["X-Forwarded-For"] = Client.ToString();
        await RunPairAsync(ctx, OptionsFor("172.22.0.0/16"));
        Assert.Equal(Client, ctx.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task SpoofedHeaders_FromUnknownPeer_AreIgnored()
    {
        // Attacker connects directly with forged XFF + CF-Connecting-IP.
        // Peer is NOT a known proxy → both headers ignored, IP + scheme unchanged.
        var ctx = Context(Stranger);
        ctx.Request.Headers["X-Forwarded-For"] = "1.2.3.4";
        ctx.Request.Headers["CF-Connecting-IP"] = "1.2.3.4";
        ctx.Request.Headers["X-Forwarded-Proto"] = "https";
        ctx.Request.Scheme = "http";
        await RunPairAsync(ctx, OptionsFor("172.22.0.0/16", "10.9.0.0/16"));
        Assert.Equal(Stranger, ctx.Connection.RemoteIpAddress);
        Assert.Equal("http", ctx.Request.Scheme);
    }

    [Fact]
    public async Task ConfigC_CloudflareHeader_PreferredOverXff()
    {
        // (c) Browser → CF edge → tunnel → NPM → frontend → backend.
        // XFF arrives mangled (missing the client); trusted CF-Connecting-IP repairs it.
        var ctx = Context(Frontend);
        ctx.Request.Headers["X-Forwarded-For"] = $"{Npm}";
        ctx.Request.Headers["CF-Connecting-IP"] = Client.ToString();
        await RunPairAsync(ctx, OptionsFor("172.22.0.0/16", "10.9.0.0/16"));
        Assert.Equal(Client, ctx.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task Scheme_FollowsXForwardedProto_FromTrustedProxy()
    {
        var ctx = Context(Npm);
        ctx.Request.Headers["X-Forwarded-Proto"] = "https";
        ctx.Request.Scheme = "http";
        await RunPairAsync(ctx, OptionsFor("10.9.0.0/16"));
        Assert.Equal("https", ctx.Request.Scheme);
    }

    [Fact]
    public async Task InvalidCfHeader_IsIgnored()
    {
        var ctx = Context(Frontend);
        ctx.Request.Headers["X-Forwarded-For"] = Client.ToString();
        ctx.Request.Headers["CF-Connecting-IP"] = "not-an-ip";
        await RunPairAsync(ctx, OptionsFor("172.22.0.0/16"));
        Assert.Equal(Client, ctx.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task KestrelMappedPeer_XffHonoredLikeIpv4()
    {
        // Live Kestrel reports container peers as ::ffff:172.22.0.5 (reproduced
        // against compose: XFF ignored until the mapped /108+ counterpart was added).
        var ctx = Context(System.Net.IPAddress.Parse("::ffff:172.22.0.5"));
        ctx.Request.Headers["X-Forwarded-For"] = Client.ToString();
        await RunPairAsync(ctx, OptionsFor("172.22.0.0/16"));
        Assert.Equal(Client, ctx.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task EnvStyleCsvSingleString_BindsAndResolves()
    {
        // Env vars bind as ONE scalar ("a,b,c") — must be split, else the whole
        // string fails CIDR parse and headers are silently ignored (fail-closed).
        var options = new ProxyOptions
        {
            KnownNetworks = "172.16.0.0/12,10.0.0.0/8,192.168.0.0/16",
            ForwardLimit = 2,
        };
        var ctx = Context(System.Net.IPAddress.Parse("::ffff:172.22.0.5"));
        ctx.Request.Headers["X-Forwarded-For"] = Client.ToString();
        await RunPairAsync(ctx, options);
        Assert.Equal(Client, ctx.Connection.RemoteIpAddress);
    }

    [Fact]
    public void IsKnownProxy_HandlesIpv4MappedIpv6()
    {
        // Kestrel reports container peers as ::ffff:172.22.0.5 — must match 172.22.0.0/16.
        var options = OptionsFor("172.22.0.0/16");
        Assert.True(options.IsKnownProxy(IPAddress.Parse("::ffff:172.22.0.5")));
        Assert.False(options.IsKnownProxy(Client));
        Assert.False(options.IsKnownProxy(null));
    }

    [Fact]
    public void ConfigBinding_SingleCsvScalar_BindsToArray()
    {
        // Env provider delivers Proxy__KnownNetworks as ONE scalar — binding must
        // yield a usable array (this path was the live failure: silently empty).
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Proxy:KnownNetworks"] = "172.16.0.0/12,10.0.0.0/8,192.168.0.0/16",
                ["Proxy:ForwardLimit"] = "2",
            })
            .Build();
        var options = config.GetSection(ProxyOptions.SectionName).Get<ProxyOptions>();
        Assert.NotNull(options);
        Assert.Equal(2, options.ForwardLimit);
        Assert.True(options.IsKnownProxy(System.Net.IPAddress.Parse("::ffff:172.22.0.5")));
    }

    [Fact]
    public void IsKnownProxy_DefaultIsLoopbackOnly_FailClosed()
    {
        var options = new ProxyOptions(); // defaults
        Assert.True(options.IsKnownProxy(IPAddress.Loopback));
        Assert.False(options.IsKnownProxy(Frontend));
    }
}
