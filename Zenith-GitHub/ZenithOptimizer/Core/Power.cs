using System;
using System.Text.RegularExpressions;

namespace Zenith.Core
{
    /// <summary>Power plan management through powercfg.exe (language independent: only GUIDs and hex values are parsed).</summary>
    public static class Power
    {
        public const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
        public const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        public const string UltimateTemplate = "e9a42b02-d5df-448d-aa00-03f14749eb61";

        // Fixed GUIDs for Zenith's own copies so we never create duplicates
        public const string ZenithUltimate = "5a3e7c11-9d0b-4e2a-8f61-2b7c0d9e4a10";
        public const string ZenithHigh = "5a3e7c11-9d0b-4e2a-8f61-2b7c0d9e4a11";

        public const string SubUsb = "2a737441-1930-4402-8d77-b2bebba308a3";
        public const string UsbSelectiveSuspend = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226";
        public const string SubPcie = "501a4d13-42af-4429-9fd1-a8218c268e20";
        public const string PcieAspm = "ee12f906-d277-404b-b6da-e5fa1a576df5";

        static string Pc => Shell.Exe("powercfg.exe");
        static readonly Regex Guid = new Regex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");

        public static string ActiveScheme()
        {
            var m = Guid.Match(Shell.Run(Pc, "/getactivescheme").Output);
            return m.Success ? m.Value.ToLowerInvariant() : null;
        }

        static bool SchemeExists(string guid) =>
            Shell.Run(Pc, "/list").Output.IndexOf(guid, StringComparison.OrdinalIgnoreCase) >= 0;

        public static bool IsPerformancePlanActive()
        {
            string a = ActiveScheme();
            return a == ZenithUltimate || a == UltimateTemplate || a == ZenithHigh || a == HighPerformance;
        }

        public static void ActivatePerformancePlan()
        {
            if (TryActivate(ZenithUltimate, UltimateTemplate)) return;
            if (TryActivate(ZenithHigh, HighPerformance)) return;
            throw new InvalidOperationException(
                "This PC only allows the Balanced plan (Modern Standby devices). The other power tweaks still apply.");
        }

        static bool TryActivate(string ours, string template)
        {
            if (!SchemeExists(ours)) Shell.Run(Pc, $"/duplicatescheme {template} {ours}");
            if (!SchemeExists(ours)) return false;
            Shell.Run(Pc, $"/changename {ours} \"Zenith {(template == UltimateTemplate ? "Ultimate" : "High")} Performance\"");
            Shell.Run(Pc, $"/setactive {ours}");
            return ActiveScheme() == ours;
        }

        public static void ActivateBalanced() => Shell.Run(Pc, $"/setactive {Balanced}");

        /// <summary>Current AC value of a setting in the active plan.</summary>
        public static int? GetAc(string subgroup, string setting)
        {
            var ms = Regex.Matches(Shell.Run(Pc, $"/q scheme_current {subgroup} {setting}").Output, @"0x([0-9a-fA-F]{8})");
            // Output ends with "Current AC Power Setting Index" then "Current DC Power Setting Index"
            if (ms.Count < 2) return null;
            return Convert.ToInt32(ms[ms.Count - 2].Groups[1].Value, 16);
        }

        public static void SetBoth(string subgroup, string setting, int value)
        {
            Shell.Run(Pc, $"/setacvalueindex scheme_current {subgroup} {setting} {value}");
            Shell.Run(Pc, $"/setdcvalueindex scheme_current {subgroup} {setting} {value}");
        }

        public static void SetAcDc(string subgroup, string setting, int ac, int dc)
        {
            Shell.Run(Pc, $"/setacvalueindex scheme_current {subgroup} {setting} {ac}");
            Shell.Run(Pc, $"/setdcvalueindex scheme_current {subgroup} {setting} {dc}");
        }

        public static void Reapply() => Shell.Run(Pc, "/setactive scheme_current");

        public static void Hibernate(bool on) => Shell.Run(Pc, on ? "/hibernate on" : "/hibernate off");
    }
}
