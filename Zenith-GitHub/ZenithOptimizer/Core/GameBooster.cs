using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace Zenith.Core
{
    public sealed class Game : Observable
    {
        bool _isBoosted, _isRunning;

        public Game(GameEntry entry) { Entry = entry; }

        public GameEntry Entry { get; }
        public string Name => Entry.Name;
        public string ExePath => Entry.ExePath;
        public string ExeName => Path.GetFileName(Entry.ExePath);
        public bool Exists => File.Exists(Entry.ExePath);

        /// <summary>High CPU priority + high-performance GPU are set for this game.</summary>
        public bool IsBoosted { get => _isBoosted; set => Set(ref _isBoosted, value); }
        public bool IsRunning { get => _isRunning; set { if (Set(ref _isRunning, value)) Raise(nameof(Status)); } }

        public string Status => IsRunning ? "Running · boosted" : ExeName;

        public void Refresh() => IsBoosted = GameBooster.IsPersistentBoostOn(ExePath);
    }

    /// <summary>
    /// Per-game boosts that Windows itself applies every time the game starts (works even when Zenith is closed):
    ///  • CPU priority "High" via Image File Execution Options\PerfOptions
    ///  • "High performance" GPU via Settings → Graphics (DirectX UserGpuPreferences)
    /// </summary>
    public static class GameBooster
    {
        const string Ifeo = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\";
        const string GpuPrefs = @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences";

        static string PerfKey(string exePath) => Ifeo + Path.GetFileName(exePath) + @"\PerfOptions";

        public static bool IsPersistentBoostOn(string exePath) =>
            Reg.GetInt(PerfKey(exePath), "CpuPriorityClass") == 3 &&
            (Reg.Get(GpuPrefs, exePath) as string ?? "").Contains("GpuPreference=2");

        public static void ApplyPersistentBoost(string exePath)
        {
            Reg.Set(PerfKey(exePath), "CpuPriorityClass", 3); // 3 = High
            Reg.Set(GpuPrefs, exePath, "GpuPreference=2;"); // 2 = High performance GPU
            Log.Write("Game boost on: " + exePath);
        }

        public static void RemovePersistentBoost(string exePath)
        {
            Reg.Delete(PerfKey(exePath), "CpuPriorityClass");
            Reg.Delete(GpuPrefs, exePath);
            Log.Write("Game boost off: " + exePath);
        }

        // ---------------- Game detection ----------------

        static readonly string[] HelperWords =
        {
            "unins", "setup", "install", "crash", "report", "launcher", "helper", "redist", "vcredist", "dxsetup",
            "easyanticheat", "battleye", "beservice", "eac", "update", "bootstrap", "cefprocess", "webhelper", "overlay", "prereq"
        };

        public static List<GameEntry> Detect()
        {
            var found = new List<GameEntry>();
            try { found.AddRange(DetectEpic()); } catch (Exception ex) { Log.Write("Epic detect: " + ex.Message); }
            try { found.AddRange(DetectSteam()); } catch (Exception ex) { Log.Write("Steam detect: " + ex.Message); }
            return found
                .Where(g => File.Exists(g.ExePath))
                .GroupBy(g => g.ExePath, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                .OrderBy(g => g.Name)
                .ToList();
        }

        static IEnumerable<GameEntry> DetectEpic()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                @"Epic\EpicGamesLauncher\Data\Manifests");
            if (!Directory.Exists(dir)) yield break;
            foreach (var file in Directory.GetFiles(dir, "*.item"))
            {
                GameEntry entry = null;
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(file));
                    var root = doc.RootElement;
                    string name = root.TryGetProperty("DisplayName", out var n) ? n.GetString() : null;
                    string loc = root.TryGetProperty("InstallLocation", out var l) ? l.GetString() : null;
                    string exe = root.TryGetProperty("LaunchExecutable", out var e) ? e.GetString() : null;
                    if (!string.IsNullOrEmpty(loc) && !string.IsNullOrEmpty(exe))
                    {
                        string path = Path.GetFullPath(Path.Combine(loc, exe));
                        // Fortnite and other UE games launch a small stub that starts the real "-Shipping" exe
                        string shipping = FindShippingExe(Path.GetDirectoryName(path));
                        entry = new GameEntry { Name = name ?? Path.GetFileNameWithoutExtension(path), ExePath = shipping ?? path };
                    }
                }
                catch { }
                if (entry != null) yield return entry;
            }
        }

        static string FindShippingExe(string dir)
        {
            try
            {
                return Directory.GetFiles(dir, "*-Shipping.exe")
                    .Where(f => !Path.GetFileName(f).Contains("EAC", StringComparison.OrdinalIgnoreCase)
                             && !Path.GetFileName(f).Contains("BE", StringComparison.Ordinal))
                    .OrderByDescending(f => new FileInfo(f).Length)
                    .FirstOrDefault();
            }
            catch { return null; }
        }

        static IEnumerable<GameEntry> DetectSteam()
        {
            string steam = Reg.Get(@"HKCU\Software\Valve\Steam", "SteamPath") as string
                        ?? Reg.Get(@"HKLM\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath") as string;
            if (string.IsNullOrEmpty(steam)) yield break;
            steam = steam.Replace('/', '\\');

            var libraries = new List<string> { steam };
            string vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
            if (File.Exists(vdf))
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));

            foreach (var lib in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string apps = Path.Combine(lib, "steamapps");
                if (!Directory.Exists(apps)) continue;
                foreach (var acf in Directory.GetFiles(apps, "appmanifest_*.acf"))
                {
                    GameEntry entry = null;
                    try
                    {
                        string text = File.ReadAllText(acf);
                        string name = Regex.Match(text, "\"name\"\\s+\"([^\"]+)\"").Groups[1].Value;
                        string installDir = Regex.Match(text, "\"installdir\"\\s+\"([^\"]+)\"").Groups[1].Value;
                        if (name.Length == 0 || installDir.Length == 0) continue;
                        if (Regex.IsMatch(name, "Steamworks|Redistributable|Proton|SteamVR|Soundtrack", RegexOptions.IgnoreCase)) continue;
                        string exe = GuessMainExe(Path.Combine(apps, "common", installDir));
                        if (exe != null) entry = new GameEntry { Name = name, ExePath = exe };
                    }
                    catch { }
                    if (entry != null) yield return entry;
                }
            }
        }

        /// <summary>Picks the largest non-helper .exe within 3 folder levels (UE "-Shipping" exes preferred).</summary>
        static string GuessMainExe(string dir)
        {
            if (!Directory.Exists(dir)) return null;
            var candidates = new List<FileInfo>();
            void Walk(string d, int depth)
            {
                try
                {
                    foreach (var f in Directory.GetFiles(d, "*.exe")) candidates.Add(new FileInfo(f));
                    if (depth < 3) foreach (var s in Directory.GetDirectories(d)) Walk(s, depth + 1);
                }
                catch { }
            }
            Walk(dir, 0);
            var good = candidates.Where(f => !HelperWords.Any(w => f.Name.Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
            var shipping = good.Where(f => f.Name.EndsWith("-Shipping.exe", StringComparison.OrdinalIgnoreCase)).OrderByDescending(f => f.Length).FirstOrDefault();
            return (shipping ?? good.OrderByDescending(f => f.Length).FirstOrDefault())?.FullName;
        }
    }

    /// <summary>
    /// Live boost while Zenith runs (window or tray): when a boosted game starts it frees RAM, raises the game's priority,
    /// lowers known background hogs, and keeps RAM available during play. Everything is restored when the game closes.
    /// </summary>
    public static class BoostMonitor
    {
        static readonly string[] BackgroundApps = { "OneDrive", "ms-teams", "Teams", "PhoneExperienceHost", "Widgets", "WidgetService", "SearchApp", "OfficeClickToRun" };

        static Timer _timer;
        static readonly HashSet<string> Active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static DateTime _lastPurge = DateTime.MinValue;
        static int _busy;

        public static event Action<string> GameStarted;
        public static event Action<string> GameStopped;

        public static bool IsRunning => _timer != null;
        public static IReadOnlyCollection<string> ActiveGames => Active;

        public static void Start()
        {
            if (_timer != null) return;
            _timer = new Timer(_ => Tick(), null, 1000, 3000);
            Log.Write("Boost monitor started");
        }

        public static void Stop()
        {
            _timer?.Dispose();
            _timer = null;
            foreach (var g in Active.ToList()) OnStopped(g);
            Log.Write("Boost monitor stopped");
        }

        static void Tick()
        {
            if (Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                var games = SettingsStore.Current.Games.ToList();
                foreach (var g in games)
                {
                    string proc = Path.GetFileNameWithoutExtension(g.ExePath);
                    bool running = Process.GetProcessesByName(proc).Length > 0;
                    if (running && Active.Add(g.Name)) OnStarted(g, proc);
                    else if (!running && Active.Contains(g.Name)) OnStopped(g.Name);
                }

                // Keep memory available during play (like ISLC): purge standby list when free RAM runs low
                if (Active.Count > 0 && (DateTime.Now - _lastPurge).TotalMinutes >= 2)
                {
                    var m = Native.Memory();
                    if (m.ullAvailPhys < m.ullTotalPhys / 6)
                    {
                        Maintenance.FreeMemory();
                        _lastPurge = DateTime.Now;
                    }
                }
            }
            catch (Exception ex) { Log.Write("Boost tick: " + ex.Message); }
            finally { Interlocked.Exchange(ref _busy, 0); }
        }

        static void OnStarted(GameEntry g, string procName)
        {
            Log.Write("Game started: " + g.Name);
            foreach (var p in Process.GetProcessesByName(procName))
                try { p.PriorityClass = ProcessPriorityClass.High; } catch { /* anti-cheat may block this; IFEO already handles it */ }
            SetBackgroundPriority(ProcessPriorityClass.BelowNormal);
            Maintenance.FreeMemory();
            _lastPurge = DateTime.Now;
            GameStarted?.Invoke(g.Name);
        }

        static void OnStopped(string name)
        {
            Active.Remove(name);
            if (Active.Count == 0) SetBackgroundPriority(ProcessPriorityClass.Normal);
            Log.Write("Game stopped: " + name);
            GameStopped?.Invoke(name);
        }

        static void SetBackgroundPriority(ProcessPriorityClass priority)
        {
            foreach (var name in BackgroundApps)
                foreach (var p in Process.GetProcessesByName(name))
                    try { p.PriorityClass = priority; } catch { }
        }

        // ---------------- Start with Windows (elevated, via Task Scheduler) ----------------

        const string TaskName = "Zenith Optimizer (tray)";

        public static bool IsAutoStartEnabled() =>
            Shell.Run(Shell.Exe("schtasks.exe"), $"/query /tn \"{TaskName}\"").Ok;

        public static bool SetAutoStart(bool on)
        {
            if (!on) return Shell.Run(Shell.Exe("schtasks.exe"), $"/delete /tn \"{TaskName}\" /f").Ok;
            string exe = Environment.ProcessPath;
            return Shell.Run(Shell.Exe("schtasks.exe"),
                $"/create /tn \"{TaskName}\" /tr \"\\\"{exe}\\\" --tray\" /sc onlogon /rl highest /f").Ok;
        }
    }
}
