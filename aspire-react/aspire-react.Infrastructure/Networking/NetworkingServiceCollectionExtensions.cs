using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace aspire_react.Server.Infrastructure.Networking;

/// <summary>
/// [REVERSE-PROXY] Registers trusted-proxy header handling (Task Q: DI lives in
/// layer extensions, not Program.cs). Program.cs only calls
/// <c>app.UseProxyHeaders()</c> first in the pipeline.
/// </summary>
public static class NetworkingServiceCollectionExtensions
{
    public static IServiceCollection AddProxyHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ProxyOptions>(configuration.GetSection(ProxyOptions.SectionName));

        // Stock middleware resolves X-Forwarded-For/-Proto; CloudflareIpMiddleware
        // (registered in the pipeline BEFORE this) folds trusted CF-Connecting-IP
        // into X-Forwarded-For first.
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            var proxy = new ProxyOptions();
            configuration.GetSection(ProxyOptions.SectionName).Bind(proxy);

            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = proxy.ForwardLimit;
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
            // NOTE: HttpOverrides ships its own IPNetwork struct — parse the same
            // entries again into framework types (single source remains ProxyOptions).
            // Kestrel reports container peers IPv4-mapped (::ffff:a.b.c.d) while the
            // framework match is family-strict — therefore register the mapped /108+
            // counterpart of every IPv4 network as well (prefix + 96).
            foreach (var entry in ProxyOptions.SplitEntries(proxy.KnownNetworks))
            {
                if (entry.Contains('/'))
                {
                    var parts = entry.Split('/');
                    if (parts.Length == 2
                        && System.Net.IPAddress.TryParse(parts[0].Trim(), out var addr)
                        && int.TryParse(parts[1].Trim(), out var prefix))
                    {
                        addr = ProxyOptions.Normalize(addr);
                        options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(addr, prefix));
                        if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            var mapped = new System.Net.IPAddress(
                                new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0xFF }
                                .Concat(addr.GetAddressBytes()).ToArray());
                            options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(mapped, prefix + 96));
                        }
                    }
                }
                else if (System.Net.IPAddress.TryParse(entry, out var ip))
                {
                    options.KnownProxies.Add(ProxyOptions.Normalize(ip));
                }
            }
        });

        return services;
    }

    /// <summary>Pipeline order matters: CF fold first, then stock forwarded-headers,
    /// both BEFORE exception handler / auth so IP + scheme are correct everywhere.</summary>
    public static void UseProxyHeaders(this WebApplication app)
    {
        app.UseMiddleware<CloudflareIpMiddleware>();
        app.UseForwardedHeaders();
    }
}
