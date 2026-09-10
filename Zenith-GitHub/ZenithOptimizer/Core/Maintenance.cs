using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Zenith.Core
{
    public static class AppState
    {
        public static List<Tweak> Tweaks { get; } = TweakCatalog.Build();
        public static List<CleanTarget> CleanTargets { get; } = Cleaner.BuildTargets();
        public static DateTime LastJunkScan { get; set; } = DateTime.MinValue;
    }

    public static class Maintenance
    {
        /// <summary>Trims working sets and purges the standby list (like RAMMap "Empty Standby List").</summary>
        public static string FreeMemory()
        {
            ulong before = SystemInfo.AvailableRam;
            Native.EnablePrivilege("SeProfileSingleProcessPrivilege");
            Native.EnablePrivilege("SeIncreaseQuotaPrivilege");

            int cmd = Native.MemoryEmptyWorkingSets;
            int s1 = Native.NtSetSystemInformation(Native.SystemMemoryListInformation, ref cmd, sizeof(int));
            cmd = Native.MemoryFlushModifiedList;
            Native.NtSetSystemInformation(Native.SystemMemoryListInformation, ref cmd, sizeof(int));
            cmd = Native.MemoryPurgeStandbyList;
            int s2 = Native.NtSetSystemInformation(Native.SystemMemoryListInformation, ref cmd, sizeof(int));
            Thread.Sleep(600);

            ulong after = SystemInfo.AvailableRam;
            Log.Write($"FreeMemory status {s1:X}/{s2:X}");
            if (s1 != 0 && s2 != 0) return "Windows refused the memory purge request.";
            long gained = after > before ? (long)(after - before) : 0;
            return $"Freed {Format.Bytes(gained)} of RAM · {Format.Bytes((long)after)} now available.";
        }

        public static string FlushDns()
        {
            var r = Shell.Run(Shell.Exe("ipconfig.exe"), "/flushdns");
            return r.Ok ? "DNS cache flushed." : "Couldn't flush the DNS cache.";
        }

        public static string ResetNetwork()
        {
            Shell.Run(Shell.Exe("netsh.exe"), "winsock reset");
            Shell.Run(Shell.Exe("netsh.exe"), "int ip reset");
            Shell.Run(Shell.Exe("ipconfig.exe"), "/flushdns");
            return "Network stack reset. Restart your PC to finish.";
        }

        public static void RepairSystem() =>
            Shell.OpenConsole("Zenith - Repair Windows", "echo Step 1/2: DISM RestoreHealth & DISM /Online /Cleanup-Image /RestoreHealth && echo. && echo Step 2/2: System File Checker && sfc /scannow");

        public static void OptimizeDrives() =>
            Shell.OpenConsole("Zenith - Optimize drives", "defrag /C /O /U /V");

        public static void ComponentCleanup() =>
            Shell.OpenConsole("Zenith - Component store cleanup", "DISM /Online /Cleanup-Image /StartComponentCleanup /ResetBase");

        public static string RestartExplorer()
        {
            Shell.Run(Shell.Exe("taskkill.exe"), "/f /im explorer.exe");
            Thread.Sleep(2500);
            // Windows restarts the shell automatically; start it only if it didn't come back
            if (Process.GetProcessesByName("explorer").Length == 0)
                Shell.Open(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));
            return "Explorer restarted.";
        }

        public static string CreateRestorePoint()
        {
            Reg.Set(@"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore", "SystemRestorePointCreationFrequency", 0);
            var r = Shell.PowerShell(
                "Enable-ComputerRestore -Drive \"$env:SystemDrive\\\" -ErrorAction SilentlyContinue; " +
                "Checkpoint-Computer -Description 'Zenith Optimizer' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop; 'ZENITH_OK'", 600_000);
            return r.Output.Contains("ZENITH_OK") ? "Restore point created." : "Couldn't create a restore point (System Protection may be disabled by policy).";
        }

        public static void OpenLog() => Shell.Open("notepad.exe", $"\"{Log.FilePath}\"");
    }
}
