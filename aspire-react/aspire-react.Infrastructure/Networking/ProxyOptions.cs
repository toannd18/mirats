using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace aspire_react.Server.Infrastructure.Networking;

/// <summary>
/// [REVERSE-PROXY] Trusted-proxy network config — binds the "Proxy" section
/// (env: Proxy__KnownNetworks, Proxy__ForwardLimit).
///
/// Only requests whose TCP peer belongs to <see cref="KnownNetworks"/> may
/// supply X-Forwarded-For / X-Forwarded-Proto / CF-Connecting-IP; headers from
/// any other source are ignored (spoof-proof). Fail-closed default: loopback
/// only — every deployment behind a proxy MUST set KnownNetworks explicitly
/// (see DEPLOYMENT.md "Triển khai sau Nginx Proxy Manager").
/// </summary>
public sealed class ProxyOptions
{
    public const string SectionName = "Proxy";

    /// <summary>CIDR ranges and/or single IPs of trusted proxies
    /// (docker nets, NPM host, cloudflared), comma-separated. Single string
    /// (not array) because env vars bind as ONE scalar — the configuration
    /// binder does NOT split commas into arrays.</summary>
    public string KnownNetworks { get; set; } = "127.0.0.1/32, ::1/128";

    /// <summary>Max proxied hops processed from the right of X-Forwarded-For.
    /// (a) trực tiếp = 1, (b) qua NPM = 2, (c) qua tunnel = 3.</summary>
    public int ForwardLimit { get; set; } = 2;

    /// <summary>Honor CF-Connecting-IP ONLY when true (default false = the header
    /// is ALWAYS ignored). Set true ONLY in config (c) where NPM is reachable
    /// solely through the Cloudflare Tunnel (firewalled) — otherwise any client
    /// talking directly to NPM could forge the header and impersonate any IP.
    /// In (a)/(b) this stays false: the header is unconditionally dropped.</summary>
    public bool TrustCloudflare { get; set; } = false;

    /// <summary>Parse entries into (networks, single-IP proxies), normalizing
    /// IPv4-mapped IPv6 (::ffff:a.b.c.d) to IPv4 so container peer IPs match.
    /// Each entry may itself be a comma-separated list (env vars bind as one
    /// scalar — the provider does NOT split commas for arrays).</summary>
    public (List<IPNetwork> Networks, List<IPAddress> Proxies) Parse()
    {
        var networks = new List<IPNetwork>();
        var proxies = new List<IPAddress>();
        foreach (var entry in SplitEntries(KnownNetworks))
        {
            if (entry.Contains('/'))
            {
                if (IPNetwork.TryParse(entry, out var net)) networks.Add(net);
            }
            else if (IPAddress.TryParse(entry, out var ip))
            {
                proxies.Add(Normalize(ip));
            }
        }
        return (networks, proxies);
    }

    /// <summary>Split raw entries on commas (env binds CSV as one scalar).</summary>
    public static IEnumerable<string> SplitEntries(string? raw)
        => (raw ?? string.Empty).Split(',')
                       .Select(e => e.Trim())
                       .Where(e => !string.IsNullOrEmpty(e));

    /// <summary>True when the TCP peer is a trusted proxy.</summary>
    public bool IsKnownProxy(IPAddress? peer)
    {
        if (peer is null) return false;
        var p = Normalize(peer);
        var (networks, proxies) = Parse();
        if (proxies.Any(x => x.Equals(p))) return true;
        return networks.Any(n => Contains(n, p));
    }

    internal static IPAddress Normalize(IPAddress ip)
        => ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;

    private static bool Contains(IPNetwork network, IPAddress address)
    {
        // IPNetwork.Contains is family-strict — normalize both sides first.
        if (network.BaseAddress.AddressFamily != address.AddressFamily) return false;
        return network.Contains(address);
    }
}
