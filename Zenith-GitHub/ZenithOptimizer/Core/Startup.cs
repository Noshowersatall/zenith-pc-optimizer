using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace Zenith.Core
{
    public sealed class StartupEntry : Observable
    {
        bool _isEnabled;

        public string Name { get; init; }
        public string Command { get; init; }
        public string ExePath { get; init; }
        public string Publisher { get; init; }
        public string Source { get; init; }
        internal string ApprovedKey { get; init; }

        public bool IsEnabled { get => _isEnabled; set => Set(ref _isEnabled, value); }

        public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name.Substring(0, 1).ToUpperInvariant();
        public string Subtitle => string.IsNullOrEmpty(Publisher) ? Source : $"{Publisher}  ·  {Source}";
        public bool CanOpenLocation => !string.IsNullOrEmpty(ExePath) && File.Exists(ExePath);
    }

    /// <summary>
    /// Enables/disables startup apps exactly like Task Manager does (StartupApproved registry keys),
    /// so every change is reversible and visible in Task Manager and Settings.
    /// </summary>
    public static class StartupManager
    {
        const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

        public static List<StartupEntry> Load()
        {
            var list = new List<StartupEntry>();
            AddRegistry(list, @"HKCU\Software\Microsoft\Windows\CurrentVersion\Run", @"HKCU\" + Approved + @"\Run", "Current user");
            AddRegistry(list, @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", @"HKLM\" + Approved + @"\Run", "All users");
            AddRegistry(list, @"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", @"HKLM\" + Approved + @"\Run32", "All users");
            AddFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), @"HKCU\" + Approved + @"\StartupFolder", "Startup folder");
            AddFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), @"HKLM\" + Approved + @"\StartupFolder", "Startup folder (all users)");
            return list.OrderByDescending(e => e.IsEnabled).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        static void AddRegistry(List<StartupEntry> list, string runKey, string approvedKey, string source)
        {
            foreach (var kv in Reg.Values(runKey))
            {
                if (string.IsNullOrEmpty(kv.Key) || kv.Value is not string cmd || string.IsNullOrWhiteSpace(cmd)) continue;
                string exe = ExtractExe(cmd);
                list.Add(new StartupEntry
                {
                    Name = kv.Key,
                    Command = cmd,
                    ExePath = exe,
                    Publisher = PublisherOf(exe),
                    Source = source,
                    ApprovedKey = approvedKey,
                    IsEnabled = IsApproved(approvedKey, kv.Key)
                });
            }
        }

        static void AddFolder(List<StartupEntry> list, string folder, string approvedKey, string source)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
            string[] files;
            try { files = Directory.GetFiles(folder); } catch { return; }
            foreach (var f in files)
            {
                string fileName = Path.GetFileName(f);
                if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new StartupEntry
                {
                    Name = Path.GetFileNameWithoutExtension(f),
                    Command = f,
                    ExePath = f,
                    Source = source,
                    ApprovedKey = approvedKey + "|" + fileName,
                    IsEnabled = IsApproved(approvedKey, fileName)
                });
            }
        }

        static (string Key, string Value) Split(StartupEntry e, string fallbackName)
        {
            int bar = e.ApprovedKey.IndexOf('|');
            return bar < 0 ? (e.ApprovedKey, fallbackName) : (e.ApprovedKey.Substring(0, bar), e.ApprovedKey.Substring(bar + 1));
        }

        static bool IsApproved(string approvedKey, string valueName)
        {
            // Missing value = enabled. First byte odd (0x03, 0x07…) = disabled by the user.
            return Reg.Get(approvedKey, valueName) is not byte[] data || data.Length == 0 || (data[0] & 1) == 0;
        }

        public static void SetEnabled(StartupEntry e, bool enabled)
        {
            var (key, value) = Split(e, e.Name);
            byte[] data = new byte[12];
            if (enabled) data[0] = 0x02;
            else
            {
                data[0] = 0x03;
                BitConverter.GetBytes(DateTime.Now.ToFileTime()).CopyTo(data, 4);
            }
            Reg.Set(key, value, data, RegistryValueKind.Binary);
            e.IsEnabled = IsApproved(key, value);
            Log.Write($"Startup '{e.Name}' -> {(enabled ? "enabled" : "disabled")}");
        }

        public static string ExtractExe(string command)
        {
            try
            {
                string c = Environment.ExpandEnvironmentVariables(command.Trim());
                if (c.StartsWith("\""))
                {
                    int end = c.IndexOf('"', 1);
                    return end > 1 ? c.Substring(1, end - 1) : c.Trim('"');
                }
                int i = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (i > 0) return c.Substring(0, i + 4);
                int space = c.IndexOf(' ');
                return space > 0 ? c.Substring(0, space) : c;
            }
            catch { return null; }
        }

        static string PublisherOf(string exe)
        {
            try
            {
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) return null;
                var v = FileVersionInfo.GetVersionInfo(exe);
                return string.IsNullOrWhiteSpace(v.CompanyName) ? null : v.CompanyName.Trim();
            }
            catch { return null; }
        }
    }
}
