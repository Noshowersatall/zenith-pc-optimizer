using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Zenith.Core
{
    /// <summary>All processes sharing a name (e.g. all chrome.exe), like Task Manager's grouped view.</summary>
    public sealed class ProcGroup : Observable
    {
        double _cpu;
        long _memory;
        int _count;

        public ProcGroup(string name) { Name = name; }

        public string Name { get; }
        public string ExePath { get; set; }
        public string Description { get; set; }
        public List<int> Pids { get; } = new List<int>();
        public bool IsProtected => ProcessMonitor.IsProtected(Name);

        public double Cpu { get => _cpu; set { if (Set(ref _cpu, value)) Raise(nameof(CpuText)); } }
        public long Memory { get => _memory; set { if (Set(ref _memory, value)) Raise(nameof(MemoryText)); } }
        public int Count { get => _count; set { if (Set(ref _count, value)) Raise(nameof(Title)); } }

        public string CpuText => $"{Cpu:0.0}%";
        public string MemoryText => Format.Bytes(Memory);
        public string Title => Count > 1 ? $"{DisplayName}  ({Count})" : DisplayName;
        public string DisplayName => string.IsNullOrWhiteSpace(Description) ? Name : Description;
        public string Initial => Name.Length > 0 ? Name.Substring(0, 1).ToUpperInvariant() : "?";
    }

    public sealed class ProcessMonitor
    {
        static readonly HashSet<string> Protected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Idle", "System", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso", "svchost",
            "dwm", "fontdrvhost", "Memory Compression", "MemCompression", "Secure System", "spoolsv", "MsMpEng", "sihost",
            "ctfmon", "audiodg", "WUDFHost", "ZenithOptimizer"
        };

        readonly Dictionary<int, TimeSpan> _lastCpu = new Dictionary<int, TimeSpan>();
        readonly Dictionary<int, (string Path, string Desc)> _info = new Dictionary<int, (string, string)>();
        DateTime _lastSample = DateTime.MinValue;

        public static bool IsProtected(string name) => Protected.Contains(name);

        /// <summary>Returns fresh stats per process name. CPU% is relative to the previous call.</summary>
        public Dictionary<string, (double Cpu, long Mem, List<int> Pids, string Path, string Desc)> Sample()
        {
            var now = DateTime.UtcNow;
            double elapsed = _lastSample == DateTime.MinValue ? 0 : (now - _lastSample).TotalMilliseconds;
            _lastSample = now;
            int cores = Environment.ProcessorCount;

            var result = new Dictionary<string, (double, long, List<int>, string, string)>(StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<int>();
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    int pid;
                    string name;
                    try { pid = p.Id; name = p.ProcessName; } catch { continue; }
                    if (pid == 0) continue;
                    seen.Add(pid);

                    long mem = 0;
                    double cpu = 0;
                    try { mem = p.WorkingSet64; } catch { }
                    try
                    {
                        var t = p.TotalProcessorTime;
                        if (elapsed > 0 && _lastCpu.TryGetValue(pid, out var prev))
                            cpu = (t - prev).TotalMilliseconds / (elapsed * cores) * 100.0;
                        _lastCpu[pid] = t;
                    }
                    catch { }

                    if (!_info.TryGetValue(pid, out var info))
                    {
                        string path = ImagePath(pid);
                        string desc = null;
                        try { if (path != null) desc = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim(); } catch { }
                        info = (path, desc);
                        _info[pid] = info;
                    }

                    if (!result.TryGetValue(name, out var agg)) agg = (0, 0, new List<int>(), info.Path, info.Desc);
                    agg.Item1 += Math.Max(0, cpu);
                    agg.Item2 += mem;
                    agg.Item3.Add(pid);
                    if (agg.Item4 == null) { agg.Item4 = info.Path; agg.Item5 = info.Desc; }
                    result[name] = agg;
                }
            }
            foreach (var dead in _lastCpu.Keys.Where(k => !seen.Contains(k)).ToList()) { _lastCpu.Remove(dead); _info.Remove(dead); }
            return result;
        }

        static string ImagePath(int pid)
        {
            IntPtr h = Native.OpenProcess(0x1000 /*QUERY_LIMITED_INFORMATION*/, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return Native.QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { Native.CloseHandlePublic(h); }
        }

        /// <summary>Ends every process in the group. Returns how many could not be ended.</summary>
        public static int Kill(ProcGroup g)
        {
            int failed = 0;
            foreach (var pid in g.Pids.ToList())
            {
                try { using var p = Process.GetProcessById(pid); p.Kill(); p.WaitForExit(3000); }
                catch { failed++; }
            }
            Log.Write($"End task {g.Name}: {g.Pids.Count - failed}/{g.Pids.Count}");
            return failed;
        }
    }
}
