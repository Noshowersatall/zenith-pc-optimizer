using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Zenith.Core
{
    /// <summary>A folder to clean. Pattern "*" = everything inside (recursive); any other pattern = matching files in that folder only.</summary>
    public sealed class CleanPath
    {
        public CleanPath(string dir, string pattern = "*") { Dir = dir; Pattern = pattern; }
        public string Dir { get; }
        public string Pattern { get; }
    }

    public sealed class CleanTarget : Observable
    {
        bool _isChecked;
        long _size;
        bool _scanned;

        public CleanTarget(string id, string name, string description, string glyph, bool recommended, Func<IEnumerable<CleanPath>> paths)
        {
            Id = id; Name = name; Description = description; Glyph = glyph; Recommended = recommended;
            Paths = paths; _isChecked = recommended;
        }

        public string Id { get; }
        public string Name { get; }
        public string Description { get; }
        public string Glyph { get; }
        public bool Recommended { get; }

        internal Func<IEnumerable<CleanPath>> Paths { get; }
        internal Func<long> CustomScan { get; init; }
        internal Func<long> CustomClean { get; init; }
        internal Action Before { get; init; }
        internal Action After { get; init; }

        public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }

        public long Size
        {
            get => _size;
            set { if (Set(ref _size, value)) Raise(nameof(SizeText)); }
        }

        public bool Scanned
        {
            get => _scanned;
            set { if (Set(ref _scanned, value)) Raise(nameof(SizeText)); }
        }

        public string SizeText => Scanned ? Format.Bytes(Size) : "—";
    }

    public static class Cleaner
    {
        static readonly object Gate = new object();

        public static List<CleanTarget> BuildTargets()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            CleanPath P(string root, string rel, string pattern = "*") => new CleanPath(Path.Combine(root, rel), pattern);

            return new List<CleanTarget>
            {
                new CleanTarget("temp", "Temporary files", "Leftover files apps create while running.", "\uE74D", true,
                    () => new[] { new CleanPath(Path.GetTempPath()) }),

                new CleanTarget("wintemp", "Windows temporary files", "Temporary files left behind by Windows and installers.", "\uE74D", true,
                    () => new[] { P(win, "Temp") }),

                new CleanTarget("wu", "Windows Update cache", "Update packages that have already been installed.", "\uE777", true,
                    () => new[] { P(win, @"SoftwareDistribution\Download") })
                {
                    Before = () => { Svc.Stop("wuauserv"); Svc.Stop("bits"); },
                    After = () => { Svc.Start("bits"); Svc.Start("wuauserv"); }
                },

                new CleanTarget("delivery", "Delivery Optimization cache", "Update files cached for sharing with other PCs.", "\uE753", true,
                    () => new[] { P(win, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache") }),

                new CleanTarget("thumbs", "Thumbnail & icon cache", "Picture previews Windows rebuilds automatically when needed.", "\uE8B9", true,
                    () => new[] { P(local, @"Microsoft\Windows\Explorer", "thumbcache_*.db"), P(local, @"Microsoft\Windows\Explorer", "iconcache_*.db") }),

                new CleanTarget("wer", "Error reports & crash dumps", "Crash reports and memory dumps from past errors.", "\uE7BA", true,
                    () => new[]
                    {
                        P(programData, @"Microsoft\Windows\WER\ReportArchive"),
                        P(programData, @"Microsoft\Windows\WER\ReportQueue"),
                        P(local, @"Microsoft\Windows\WER"),
                        P(local, "CrashDumps"),
                        P(win, "Minidump"),
                        new CleanPath(win, "MEMORY.DMP"),
                        P(win, "LiveKernelReports")
                    }),

                new CleanTarget("logs", "Windows log files", "Setup and servicing logs that are no longer needed.", "\uE8A5", true,
                    () => new[]
                    {
                        P(win, @"Logs\CBS", "*.log"), P(win, @"Logs\CBS", "*.cab"),
                        P(win, @"Logs\DISM", "*.log"),
                        P(win, @"Logs\MeasuredBoot", "*.log"),
                        P(win, @"Panther", "*.log"),
                        P(win, @"Logs\WindowsUpdate", "*.etl")
                    }),

                new CleanTarget("browsers", "Browser caches", "Cached web files from Chrome, Edge, Brave, Opera and Firefox. Close browsers first.", "\uE774", true,
                    () => BrowserPaths(local, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))),

                new CleanTarget("shaders", "GPU shader caches", "DirectX, NVIDIA and AMD shader caches. Games rebuild them, so the first launch after cleaning may stutter briefly.", "\uE7F4", false,
                    () => new[]
                    {
                        P(local, "D3DSCache"),
                        P(local, @"NVIDIA\DXCache"), P(local, @"NVIDIA\GLCache"),
                        P(local, @"AMD\DxCache"), P(local, @"AMD\DxcCache"), P(local, @"AMD\VkCache"), P(local, @"AMD\GLCache")
                    }),

                new CleanTarget("recycle", "Recycle Bin", "Files you've already deleted.", "\uE74D", true,
                    () => Array.Empty<CleanPath>())
                {
                    CustomScan = RecycleBinSize,
                    CustomClean = EmptyRecycleBin
                },
            };
        }

        static IEnumerable<CleanPath> BrowserPaths(string local, string roaming)
        {
            var chromium = new[]
            {
                Path.Combine(local, @"Google\Chrome\User Data"),
                Path.Combine(local, @"Microsoft\Edge\User Data"),
                Path.Combine(local, @"BraveSoftware\Brave-Browser\User Data"),
                Path.Combine(local, @"Vivaldi\User Data"),
                Path.Combine(roaming, @"Opera Software\Opera Stable"),
                Path.Combine(roaming, @"Opera Software\Opera GX Stable"),
            };
            foreach (var root in chromium.Where(Directory.Exists))
            {
                yield return new CleanPath(Path.Combine(root, "ShaderCache"));
                yield return new CleanPath(Path.Combine(root, "GrShaderCache"));
                string[] profiles;
                try { profiles = Directory.GetDirectories(root); } catch { continue; }
                foreach (var profile in profiles.Append(root))
                {
                    foreach (var sub in new[] { "Cache", "Code Cache", "GPUCache", @"Service Worker\CacheStorage" })
                    {
                        string dir = Path.Combine(profile, sub);
                        if (Directory.Exists(dir)) yield return new CleanPath(dir);
                    }
                }
            }

            string ff = Path.Combine(local, @"Mozilla\Firefox\Profiles");
            if (Directory.Exists(ff))
            {
                string[] profiles;
                try { profiles = Directory.GetDirectories(ff); } catch { yield break; }
                foreach (var p in profiles)
                {
                    yield return new CleanPath(Path.Combine(p, "cache2"));
                    yield return new CleanPath(Path.Combine(p, "startupCache"));
                }
            }
        }

        // ---------------- scanning & cleaning ----------------

        public static void ScanAll(IEnumerable<CleanTarget> targets)
        {
            lock (Gate)
            {
                foreach (var t in targets) Scan(t);
                AppState.LastJunkScan = DateTime.Now;
            }
        }

        public static long Scan(CleanTarget t)
        {
            long size = 0;
            try
            {
                if (t.CustomScan != null) size = t.CustomScan();
                else
                    foreach (var cp in t.Paths())
                        foreach (var f in Files(cp))
                            try { size += f.Length; } catch { }
            }
            catch (Exception ex) { Log.Write($"Scan {t.Id}: {ex.Message}"); }
            t.Size = size;
            t.Scanned = true;
            return size;
        }

        /// <summary>Deletes the target's files and returns the number of bytes freed. Locked files are skipped.</summary>
        public static long Clean(CleanTarget t)
        {
            lock (Gate)
            {
                long freed = 0;
                try
                {
                    t.Before?.Invoke();
                    if (t.CustomClean != null) freed = t.CustomClean();
                    else
                    {
                        foreach (var cp in t.Paths().ToList())
                        {
                            if (!IsSafe(cp.Dir)) continue;
                            foreach (var f in Files(cp).ToList())
                            {
                                try
                                {
                                    long len = f.Length;
                                    if ((f.Attributes & FileAttributes.ReadOnly) != 0) f.Attributes &= ~FileAttributes.ReadOnly;
                                    f.Delete();
                                    freed += len;
                                }
                                catch { /* in use or protected — skip */ }
                            }
                            if (cp.Pattern == "*") RemoveEmptyDirs(cp.Dir);
                        }
                    }
                }
                catch (Exception ex) { Log.Write($"Clean {t.Id}: {ex.Message}"); }
                finally
                {
                    try { t.After?.Invoke(); } catch { }
                }
                Log.Write($"Cleaned {t.Id}: {Format.Bytes(freed)}");
                Scan(t);
                return freed;
            }
        }

        static bool IsSafe(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return false;
            string full;
            try { full = Path.GetFullPath(dir).TrimEnd('\\'); } catch { return false; }
            string root = (Path.GetPathRoot(full) ?? "").TrimEnd('\\');
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');
            // Never operate on a drive root, the Windows folder itself or the user profile root
            return full.Length > 8
                && !full.Equals(root, StringComparison.OrdinalIgnoreCase)
                && !full.Equals(win, StringComparison.OrdinalIgnoreCase)
                && !full.Equals(profile, StringComparison.OrdinalIgnoreCase);
        }

        static IEnumerable<FileInfo> Files(CleanPath cp)
        {
            if (!IsSafe(cp.Dir)) yield break;
            bool recursive = cp.Pattern == "*";
            var stack = new Stack<string>();
            stack.Push(cp.Dir);
            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                FileInfo[] files;
                DirectoryInfo[] subs = Array.Empty<DirectoryInfo>();
                try
                {
                    var di = new DirectoryInfo(dir);
                    if (!di.Exists) continue;
                    files = di.GetFiles(cp.Pattern);
                    if (recursive) subs = di.GetDirectories();
                }
                catch { continue; }

                foreach (var f in files) yield return f;
                foreach (var s in subs)
                {
                    // never follow junctions / symlinks out of the cache folder
                    if ((s.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    stack.Push(s.FullName);
                }
            }
        }

        static void RemoveEmptyDirs(string root)
        {
            try
            {
                var all = new List<string>();
                var stack = new Stack<string>();
                stack.Push(root);
                while (stack.Count > 0)
                {
                    string d = stack.Pop();
                    string[] subs;
                    try { subs = Directory.GetDirectories(d); } catch { continue; }
                    foreach (var s in subs)
                    {
                        try { if ((File.GetAttributes(s) & FileAttributes.ReparsePoint) != 0) continue; } catch { continue; }
                        all.Add(s);
                        stack.Push(s);
                    }
                }
                foreach (var d in all.OrderByDescending(x => x.Length))
                    try { Directory.Delete(d, false); } catch { }
            }
            catch { }
        }

        static long RecycleBinSize()
        {
            var info = new Native.SHQUERYRBINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.SHQUERYRBINFO>() };
            return Native.SHQueryRecycleBin(null, ref info) == 0 ? info.i64Size : 0;
        }

        static long EmptyRecycleBin()
        {
            long size = RecycleBinSize();
            if (size == 0) return 0;
            Native.SHEmptyRecycleBin(IntPtr.Zero, null, Native.SHERB_NOCONFIRMATION | Native.SHERB_NOPROGRESSUI | Native.SHERB_NOSOUND);
            return size - RecycleBinSize();
        }
    }
}
