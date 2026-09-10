using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Zenith.Core
{
    public sealed class InstalledApp : Observable
    {
        public string Name { get; init; }
        public string Publisher { get; init; }
        public string Version { get; init; }
        public long SizeBytes { get; init; }
        public DateTime? InstallDate { get; init; }
        public string UninstallString { get; init; }
        public string IconPath { get; init; }
        internal string KeyPath { get; init; }

        public string SizeText => SizeBytes > 0 ? Format.Bytes(SizeBytes) : "";
        public string Subtitle
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(Publisher)) parts.Add(Publisher.Trim());
                if (!string.IsNullOrWhiteSpace(Version)) parts.Add("v" + Version.Trim());
                if (InstallDate.HasValue) parts.Add("Installed " + InstallDate.Value.ToString("MMM d, yyyy", CultureInfo.InvariantCulture));
                return string.Join("  ·  ", parts);
            }
        }
        public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name.Substring(0, 1).ToUpperInvariant();
    }

    public static class Uninstaller
    {
        static readonly string[] Roots =
        {
            @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            @"HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall",
        };

        public static List<InstalledApp> Load()
        {
            var apps = new List<InstalledApp>();
            foreach (var root in Roots)
            {
                foreach (var sub in Reg.SubKeys(root))
                {
                    string key = root + "\\" + sub;
                    var values = Reg.Values(key).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
                    string name = Str(values, "DisplayName");
                    string uninstall = Str(values, "UninstallString");
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(uninstall)) continue;
                    if (values.TryGetValue("SystemComponent", out var sc) && sc is int sci && sci == 1) continue;
                    if (!string.IsNullOrEmpty(Str(values, "ParentKeyName"))) continue; // updates/patches
                    string release = Str(values, "ReleaseType") ?? "";
                    if (release.Contains("Update") || release.Contains("Hotfix")) continue;

                    DateTime? date = null;
                    string d = Str(values, "InstallDate");
                    if (d != null && DateTime.TryParseExact(d, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) date = parsed;

                    long size = values.TryGetValue("EstimatedSize", out var es) && es is int kb ? kb * 1024L : 0;

                    apps.Add(new InstalledApp
                    {
                        Name = name.Trim(),
                        Publisher = Str(values, "Publisher"),
                        Version = Str(values, "DisplayVersion"),
                        SizeBytes = size,
                        InstallDate = date,
                        UninstallString = uninstall.Trim(),
                        IconPath = IconFrom(Str(values, "DisplayIcon")) ?? IconFrom(StartupManager.ExtractExe(uninstall)),
                        KeyPath = key
                    });
                }
            }
            return apps
                .GroupBy(a => a.Name + "|" + a.Version, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        static string Str(Dictionary<string, object> v, string name) =>
            v.TryGetValue(name, out var o) ? Environment.ExpandEnvironmentVariables(o?.ToString() ?? "") : null;

        static string IconFrom(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string p = raw.Trim().Trim('"');
            int comma = p.LastIndexOf(',');
            if (comma > 2 && !File.Exists(p)) p = p.Substring(0, comma).Trim('"');
            if (p.EndsWith("msiexec.exe", StringComparison.OrdinalIgnoreCase)) return null;
            return File.Exists(p) ? p : null;
        }

        public static bool StillInstalled(InstalledApp a) => Reg.SubKeys(Path.GetDirectoryName(a.KeyPath)).Contains(Path.GetFileName(a.KeyPath));

        /// <summary>Launches the app's own uninstaller and waits for it to finish.</summary>
        public static async Task Uninstall(InstalledApp a)
        {
            string cmd = a.UninstallString;
            var msi = Regex.Match(cmd, @"msiexec(\.exe)?\s+/[IX]\s*(\{[0-9A-Fa-f\-]+\})", RegexOptions.IgnoreCase);
            if (msi.Success) cmd = $"\"{Shell.Exe("msiexec.exe")}\" /x {msi.Groups[2].Value}";
            Log.Write($"Uninstall {a.Name}: {cmd}");
            var psi = new ProcessStartInfo(Shell.Exe("cmd.exe"), "/c \"" + cmd + "\"") { UseShellExecute = false, CreateNoWindow = true };
            using var p = Process.Start(psi);
            if (p != null) await p.WaitForExitAsync();
            // Many uninstallers relaunch themselves from %TEMP% and exit immediately — give them a moment
            for (int i = 0; i < 20 && StillInstalled(a); i++) await Task.Delay(1500);
        }
    }
}
