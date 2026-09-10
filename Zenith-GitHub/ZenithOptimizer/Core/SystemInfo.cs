using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Zenith.Core
{
    public static class SystemInfo
    {
        static long _prevIdle, _prevTotal;

        public static string OsName { get; private set; } = "Windows";
        public static string OsVersion { get; private set; } = "";
        public static string CpuName { get; private set; } = "Unknown CPU";
        public static string GpuName { get; private set; } = "Unknown GPU";
        public static string MachineName => Environment.MachineName;
        public static int CoreCount => Environment.ProcessorCount;
        public static ulong TotalRam { get; private set; }

        public static void Load()
        {
            try
            {
                const string cv = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion";
                string product = Reg.Get(cv, "ProductName") as string ?? "Windows";
                string build = Reg.Get(cv, "CurrentBuildNumber") as string ?? "";
                string display = Reg.Get(cv, "DisplayVersion") as string ?? "";
                int ubr = Reg.GetInt(cv, "UBR") ?? 0;
                // Windows 11 still reports "Windows 10" in ProductName
                if (int.TryParse(build, out int b) && b >= 22000) product = product.Replace("Windows 10", "Windows 11");
                OsName = product;
                OsVersion = $"{display} (build {build}.{ubr})".Trim();

                string cpu = Reg.Get(@"HKLM\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString") as string;
                if (!string.IsNullOrWhiteSpace(cpu)) CpuName = Regex.Replace(cpu.Trim(), @"\s+", " ");

                const string gpuClass = @"HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
                var gpus = Reg.SubKeys(gpuClass)
                    .Where(k => k.Length == 4 && k.All(char.IsDigit))
                    .Select(k => Reg.Get(gpuClass + "\\" + k, "DriverDesc") as string)
                    .Where(n => !string.IsNullOrWhiteSpace(n) && !n.Contains("Basic Display") && !n.Contains("Remote"))
                    .Distinct()
                    .ToList();
                if (gpus.Count > 0) GpuName = string.Join(" + ", gpus);

                TotalRam = Native.Memory().ullTotalPhys;
            }
            catch (Exception ex) { Log.Write("SystemInfo.Load: " + ex.Message); }
        }

        /// <summary>Overall CPU usage in percent since the previous call.</summary>
        public static double CpuUsage()
        {
            if (!Native.GetSystemTimes(out long idle, out long kernel, out long user)) return 0;
            long total = kernel + user; // kernel time includes idle time
            long dIdle = idle - _prevIdle, dTotal = total - _prevTotal;
            _prevIdle = idle; _prevTotal = total;
            if (dTotal <= 0) return 0;
            return Math.Clamp(100.0 * (dTotal - dIdle) / dTotal, 0, 100);
        }

        public static (double Percent, ulong Used, ulong Total) Memory()
        {
            var m = Native.Memory();
            ulong used = m.ullTotalPhys - m.ullAvailPhys;
            return (m.ullTotalPhys == 0 ? 0 : 100.0 * used / m.ullTotalPhys, used, m.ullTotalPhys);
        }

        public static ulong AvailableRam => Native.Memory().ullAvailPhys;

        public static string SystemDrive => Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";

        public static (long Free, long Total) Disk()
        {
            try
            {
                var d = new DriveInfo(SystemDrive);
                return (d.AvailableFreeSpace, d.TotalSize);
            }
            catch { return (0, 0); }
        }

        /// <summary>First name of the signed-in user (Microsoft/local account display name), falling back to the login name.</summary>
        public static string UserFirstName
        {
            get
            {
                try
                {
                    var sb = new System.Text.StringBuilder(256);
                    uint size = (uint)sb.Capacity;
                    if (Native.GetUserNameEx(3 /*NameDisplay*/, sb, ref size) && sb.Length > 0)
                    {
                        string first = sb.ToString().Trim().Split(' ')[0];
                        if (first.Length > 0) return first;
                    }
                }
                catch { }
                string u = Environment.UserName;
                return string.IsNullOrEmpty(u) ? "there" : char.ToUpperInvariant(u[0]) + u.Substring(1);
            }
        }

        public static TimeSpan Uptime => TimeSpan.FromMilliseconds(Environment.TickCount64);
    }
}
