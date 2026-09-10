using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Win32;

namespace Zenith.Core
{
    /// <summary>Minimal INotifyPropertyChanged base.</summary>
    public abstract class Observable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            Raise(name);
            return true;
        }

        protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public static class Log
    {
        static readonly object Gate = new object();

        public static string FilePath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zenith", "zenith.log");

        public static void Write(string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
                }
            }
            catch { /* logging must never crash the app */ }
        }
    }

    public static class Format
    {
        public static string Bytes(long bytes)
        {
            if (bytes < 0) bytes = 0;
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes;
            int u = 0;
            while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
            return u == 0 ? $"{v:0} {units[u]}" : $"{v:0.0} {units[u]}";
        }

        public static string Duration(TimeSpan t)
        {
            if (t.TotalDays >= 1) return $"{(int)t.TotalDays}d {t.Hours}h";
            if (t.TotalHours >= 1) return $"{t.Hours}h {t.Minutes}m";
            return $"{t.Minutes}m";
        }
    }

    public readonly struct ShellResult
    {
        public ShellResult(int exitCode, string output) { ExitCode = exitCode; Output = output ?? ""; }
        public int ExitCode { get; }
        public string Output { get; }
        public bool Ok => ExitCode == 0;
    }

    /// <summary>Runs command-line tools hidden and captures their output.</summary>
    public static class Shell
    {
        public static string Sys32 => Environment.SystemDirectory;
        public static string Exe(string name) => Path.Combine(Sys32, name);

        public static ShellResult Run(string file, string args, int timeoutMs = 300_000)
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            try
            {
                using var p = Process.Start(psi);
                var stdout = p.StandardOutput.ReadToEndAsync();
                var stderr = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(true); } catch { }
                    Log.Write($"TIMEOUT {file} {args}");
                    return new ShellResult(-1, "Timed out");
                }
                p.WaitForExit();
                string output = stdout.Result + stderr.Result;
                Log.Write($"{Path.GetFileName(file)} {Truncate(args, 160)} -> {p.ExitCode}");
                return new ShellResult(p.ExitCode, output);
            }
            catch (Exception ex)
            {
                Log.Write($"FAILED {file} {args}: {ex.Message}");
                return new ShellResult(-1, ex.Message);
            }
        }

        public static ShellResult PowerShell(string script, int timeoutMs = 300_000)
        {
            string full = "$ProgressPreference='SilentlyContinue'; $ErrorActionPreference='Continue'; " + script;
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
            return Run(Exe(@"WindowsPowerShell\v1.0\powershell.exe"),
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded, timeoutMs);
        }

        /// <summary>Opens a visible console window for long-running tools (SFC, DISM, defrag).</summary>
        public static void OpenConsole(string title, string command)
        {
            Process.Start(new ProcessStartInfo(Exe("cmd.exe"), $"/k title {title} & {command}")
            {
                UseShellExecute = true
            });
        }

        public static void Open(string target, string args = null)
        {
            try
            {
                var psi = args == null ? new ProcessStartInfo(target) : new ProcessStartInfo(target, args);
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex) { Log.Write("Open failed: " + ex.Message); }
        }

        static string Truncate(string s, int n) => s.Length <= n ? s : s.Substring(0, n) + "…";
    }

    /// <summary>Registry helpers using paths like @"HKCU\Software\...".</summary>
    public static class Reg
    {
        static RegistryKey OpenBase(string path, out string subKey)
        {
            int i = path.IndexOf('\\');
            string hive = path.Substring(0, i).ToUpperInvariant();
            subKey = path.Substring(i + 1);
            RegistryHive h = hive switch
            {
                "HKCU" or "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
                "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
                "HKCR" or "HKEY_CLASSES_ROOT" => RegistryHive.ClassesRoot,
                "HKU" or "HKEY_USERS" => RegistryHive.Users,
                _ => throw new ArgumentException("Unknown hive: " + hive)
            };
            return RegistryKey.OpenBaseKey(h, RegistryView.Registry64);
        }

        public static object Get(string path, string name)
        {
            try
            {
                using var root = OpenBase(path, out var sub);
                using var key = root.OpenSubKey(sub);
                return key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            }
            catch { return null; }
        }

        public static int? GetInt(string path, string name) => Get(path, name) is int i ? i : (int?)null;

        public static void Set(string path, string name, object value, RegistryValueKind? kind = null)
        {
            using var root = OpenBase(path, out var sub);
            using var key = root.CreateSubKey(sub, true);
            RegistryValueKind k = kind ?? value switch
            {
                int => RegistryValueKind.DWord,
                long => RegistryValueKind.QWord,
                byte[] => RegistryValueKind.Binary,
                string[] => RegistryValueKind.MultiString,
                _ => RegistryValueKind.String
            };
            key.SetValue(name, value, k);
        }

        public static void Delete(string path, string name)
        {
            try
            {
                using var root = OpenBase(path, out var sub);
                using var key = root.OpenSubKey(sub, true);
                key?.DeleteValue(name, false);
            }
            catch (Exception ex) { Log.Write($"Reg delete failed {path}\\{name}: {ex.Message}"); }
        }

        public static List<KeyValuePair<string, object>> Values(string path)
        {
            var list = new List<KeyValuePair<string, object>>();
            try
            {
                using var root = OpenBase(path, out var sub);
                using var key = root.OpenSubKey(sub);
                if (key == null) return list;
                foreach (var n in key.GetValueNames())
                    list.Add(new KeyValuePair<string, object>(n, key.GetValue(n, null, RegistryValueOptions.DoNotExpandEnvironmentNames)));
            }
            catch { }
            return list;
        }

        public static string[] SubKeys(string path)
        {
            try
            {
                using var root = OpenBase(path, out var sub);
                using var key = root.OpenSubKey(sub);
                return key?.GetSubKeyNames() ?? Array.Empty<string>();
            }
            catch { return Array.Empty<string>(); }
        }

        public static bool ValueEquals(object current, object expected)
        {
            if (current == null || expected == null) return current == null && expected == null;
            if (expected is int ei) return current is int ci && ci == ei;
            if (expected is byte[] eb) return current is byte[] cb && cb.SequenceEqual(eb);
            return string.Equals(current.ToString(), expected.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Windows service helpers (start type read from the registry, changes via sc.exe).</summary>
    public static class Svc
    {
        static string Key(string name) => $@"HKLM\SYSTEM\CurrentControlSet\Services\{name}";

        public static int? StartType(string name) => Reg.GetInt(Key(name), "Start");
        public static bool Exists(string name) => StartType(name).HasValue;
        public static bool IsDisabledOrMissing(string name) => !Exists(name) || StartType(name) == 4;

        public static void Disable(string name)
        {
            if (!Exists(name)) return;
            Shell.Run(Shell.Exe("sc.exe"), $"config \"{name}\" start= disabled");
            Shell.Run(Shell.Exe("sc.exe"), $"stop \"{name}\"");
        }

        /// <param name="start">auto, delayed-auto or demand</param>
        public static void Enable(string name, string start)
        {
            if (!Exists(name)) return;
            Shell.Run(Shell.Exe("sc.exe"), $"config \"{name}\" start= {start}");
            if (start != "demand") Shell.Run(Shell.Exe("sc.exe"), $"start \"{name}\"");
        }

        public static void Stop(string name) => Shell.Run(Shell.Exe("net.exe"), $"stop \"{name}\" /y", 60_000);
        public static void Start(string name) => Shell.Run(Shell.Exe("net.exe"), $"start \"{name}\"", 60_000);
    }
}
