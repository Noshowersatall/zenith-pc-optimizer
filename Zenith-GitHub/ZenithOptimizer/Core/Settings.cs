using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Zenith.Core
{
    public sealed class GameEntry
    {
        public string Name { get; set; }
        public string ExePath { get; set; }
    }

    public sealed class BenchResult
    {
        public DateTime Date { get; set; }
        public double CpuSingle { get; set; }
        public double CpuMulti { get; set; }
        public double RamGBs { get; set; }
        public double DiskReadMBs { get; set; }
        public double DiskWriteMBs { get; set; }
        public double Disk4kIops { get; set; }
        public double? BootSeconds { get; set; }
    }

    public sealed class Settings
    {
        public List<GameEntry> Games { get; set; } = new List<GameEntry>();
        public bool AutoBoost { get; set; } = true;
        public bool TrayOnClose { get; set; } = true;
        public List<BenchResult> Benchmarks { get; set; } = new List<BenchResult>();
    }

    /// <summary>Persists settings to %LocalAppData%\Zenith\settings.json.</summary>
    public static class SettingsStore
    {
        static readonly object Gate = new object();
        static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zenith", "settings.json");

        public static Settings Current { get; } = Load();

        static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
            }
            catch (Exception ex) { Log.Write("Settings load: " + ex.Message); }
            return new Settings();
        }

        public static void Save()
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
                }
            }
            catch (Exception ex) { Log.Write("Settings save: " + ex.Message); }
        }
    }
}
