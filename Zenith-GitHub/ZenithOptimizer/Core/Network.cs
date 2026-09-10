using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Zenith.Core
{
    public sealed class DnsProvider : Observable
    {
        string _latency = "—";
        bool _isCurrent, _isFastest;

        public string Id { get; init; }
        public string Name { get; init; }
        public string Description { get; init; }
        public string[] V4 { get; init; } = Array.Empty<string>();
        public string[] V6 { get; init; } = Array.Empty<string>();
        public string Addresses => V4.Length == 0 ? "Uses your router / ISP" : string.Join("  ·  ", V4);

        public string Latency { get => _latency; set => Set(ref _latency, value); }
        public bool IsCurrent { get => _isCurrent; set => Set(ref _isCurrent, value); }
        public bool IsFastest { get => _isFastest; set => Set(ref _isFastest, value); }
    }

    public static class DnsSwitcher
    {
        public static List<DnsProvider> Providers { get; } = new List<DnsProvider>
        {
            new DnsProvider { Id = "auto", Name = "Automatic (ISP)", Description = "Whatever your router or internet provider hands out. Windows default." },
            new DnsProvider { Id = "cloudflare", Name = "Cloudflare", Description = "Usually the fastest worldwide, privacy-focused (no logging of your IP).",
                V4 = new[] { "1.1.1.1", "1.0.0.1" }, V6 = new[] { "2606:4700:4700::1111", "2606:4700:4700::1001" } },
            new DnsProvider { Id = "google", Name = "Google Public DNS", Description = "Very fast and reliable everywhere.",
                V4 = new[] { "8.8.8.8", "8.8.4.4" }, V6 = new[] { "2001:4860:4860::8888", "2001:4860:4860::8844" } },
            new DnsProvider { Id = "quad9", Name = "Quad9", Description = "Blocks known malware and phishing domains. Non-profit.",
                V4 = new[] { "9.9.9.9", "149.112.112.112" }, V6 = new[] { "2620:fe::fe", "2620:fe::9" } },
        };

        public static List<NetworkInterface> ActiveAdapters() =>
            NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                         && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                         && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel
                         && n.GetIPProperties().GatewayAddresses.Any())
                .ToList();

        public static string CurrentServers()
        {
            try
            {
                var a = ActiveAdapters().FirstOrDefault();
                if (a == null) return "No active connection";
                var list = a.GetIPProperties().DnsAddresses
                    .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork)
                    .Select(ip => ip.ToString()).ToList();
                return list.Count == 0 ? "Automatic" : string.Join(", ", list);
            }
            catch { return "Unknown"; }
        }

        public static void DetectCurrent()
        {
            string current = CurrentServers();
            var match = Providers.FirstOrDefault(p => p.V4.Length > 0 && current.Contains(p.V4[0]));
            foreach (var p in Providers) p.IsCurrent = p == (match ?? Providers[0]);
        }

        /// <summary>Applies the provider to every active adapter (IPv4 + IPv6). Returns null on success.</summary>
        public static string Apply(DnsProvider p)
        {
            var adapters = ActiveAdapters();
            if (adapters.Count == 0) return "No active network connection found.";
            string netsh = Shell.Exe("netsh.exe");
            int failures = 0;
            foreach (var a in adapters)
            {
                string n = a.Name.Replace("\"", "");
                if (p.V4.Length == 0)
                {
                    if (!Shell.Run(netsh, $"interface ipv4 set dnsservers name=\"{n}\" source=dhcp").Ok) failures++;
                    Shell.Run(netsh, $"interface ipv6 set dnsservers name=\"{n}\" source=dhcp");
                }
                else
                {
                    if (!Shell.Run(netsh, $"interface ipv4 set dnsservers name=\"{n}\" source=static address={p.V4[0]} register=primary validate=no").Ok) failures++;
                    Shell.Run(netsh, $"interface ipv4 add dnsservers name=\"{n}\" address={p.V4[1]} index=2 validate=no");
                    if (p.V6.Length == 2)
                    {
                        Shell.Run(netsh, $"interface ipv6 set dnsservers name=\"{n}\" source=static address={p.V6[0]} register=primary validate=no");
                        Shell.Run(netsh, $"interface ipv6 add dnsservers name=\"{n}\" address={p.V6[1]} index=2 validate=no");
                    }
                }
            }
            Shell.Run(Shell.Exe("ipconfig.exe"), "/flushdns");
            Log.Write($"DNS -> {p.Name} on {adapters.Count} adapter(s), {failures} failed");
            return failures == adapters.Count ? "Windows refused the DNS change." : null;
        }

        /// <summary>Average ping in ms (3 tries), or null if unreachable.</summary>
        public static double? Ping(string host)
        {
            try
            {
                using var ping = new Ping();
                var times = new List<long>();
                for (int i = 0; i < 3; i++)
                {
                    var r = ping.Send(host, 1500);
                    if (r.Status == IPStatus.Success) times.Add(r.RoundtripTime);
                }
                return times.Count == 0 ? null : times.Average();
            }
            catch { return null; }
        }

        /// <summary>Pings each provider; for "Automatic" pings the current DNS server.</summary>
        public static void MeasureAll()
        {
            double best = double.MaxValue;
            DnsProvider fastest = null;
            foreach (var p in Providers)
            {
                string target = p.V4.Length > 0 ? p.V4[0] : FirstCurrentServer();
                double? ms = target == null ? null : Ping(target);
                p.Latency = ms.HasValue ? $"{ms.Value:0} ms" : "no reply";
                if (ms.HasValue && ms.Value < best) { best = ms.Value; fastest = p; }
            }
            foreach (var p in Providers) p.IsFastest = p == fastest;
        }

        static string FirstCurrentServer()
        {
            string s = CurrentServers();
            return s.Contains('.') ? s.Split(',')[0].Trim() : null;
        }
    }
}
