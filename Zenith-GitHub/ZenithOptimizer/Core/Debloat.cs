using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Zenith.Core
{
    public sealed class BloatApp : Observable
    {
        bool _isChecked;
        public string Pattern { get; init; }
        public string Name { get; init; }
        public string Note { get; init; }
        public bool Recommended { get; init; }
        public string PackageName { get; init; }
        internal bool IsOneDrive { get; init; }

        public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }
        public bool HasNote => !string.IsNullOrEmpty(Note);
    }

    public static class Debloater
    {
        // Pattern (PowerShell -like syntax), friendly name, recommended to remove, note
        static readonly (string Pattern, string Name, bool Rec, string Note)[] Catalog =
        {
            ("Microsoft.BingNews", "Microsoft News", true, null),
            ("Microsoft.BingWeather", "Weather", false, null),
            ("Microsoft.BingSearch", "Bing Search", true, null),
            ("Microsoft.BingFinance", "Money", true, null),
            ("Microsoft.BingSports", "Sports", true, null),
            ("Microsoft.Copilot", "Copilot", true, null),
            ("Microsoft.Windows.Ai.Copilot.Provider", "Copilot provider", true, null),
            ("Microsoft.GetHelp", "Get Help", true, null),
            ("Microsoft.Getstarted", "Tips", true, null),
            ("Microsoft.MicrosoftSolitaireCollection", "Solitaire Collection", true, null),
            ("Microsoft.MicrosoftOfficeHub", "Microsoft 365 (Office) hub", true, null),
            ("Microsoft.People", "People", true, null),
            ("Microsoft.PowerAutomateDesktop", "Power Automate", true, null),
            ("Microsoft.WindowsFeedbackHub", "Feedback Hub", true, null),
            ("Microsoft.WindowsMaps", "Maps", true, null),
            ("Microsoft.ZuneVideo", "Movies & TV", true, null),
            ("Clipchamp.Clipchamp", "Clipchamp video editor", true, null),
            ("Microsoft.549981C3F5F10", "Cortana", true, null),
            ("Microsoft.Windows.DevHome", "Dev Home", true, null),
            ("MSTeams", "Microsoft Teams (personal)", true, "Remove only if you don't use Teams."),
            ("MicrosoftTeams", "Microsoft Teams (classic personal)", true, null),
            ("Microsoft.SkypeApp", "Skype", true, null),
            ("Microsoft.MixedReality.Portal", "Mixed Reality Portal", true, null),
            ("Microsoft.Microsoft3DViewer", "3D Viewer", true, null),
            ("Microsoft.3DBuilder", "3D Builder", true, null),
            ("Microsoft.Print3D", "Print 3D", true, null),
            ("Microsoft.Messaging", "Messaging", true, null),
            ("Microsoft.OneConnect", "Mobile Plans", true, null),
            ("MicrosoftCorporationII.MicrosoftFamily", "Family Safety", true, null),
            ("Microsoft.Todos", "Microsoft To Do", false, null),
            ("Microsoft.OutlookForWindows", "Outlook (new)", false, "Your email app — keep it if you use it."),
            ("microsoft.windowscommunicationsapps", "Mail and Calendar", false, "Your email app — keep it if you use it."),
            ("Microsoft.YourPhone", "Phone Link", false, "Connects your phone to the PC."),
            ("Microsoft.MicrosoftStickyNotes", "Sticky Notes", false, null),
            ("Microsoft.WindowsSoundRecorder", "Sound Recorder", false, null),
            ("Microsoft.WindowsAlarms", "Clock & Alarms", false, null),
            ("Microsoft.GamingApp", "Xbox app", false, "Needed for PC Game Pass."),
            ("MicrosoftCorporationII.QuickAssist", "Quick Assist", false, null),
            ("Microsoft.Office.OneNote", "OneNote for Windows 10", false, null),
            ("7EE7776C.LinkedInforWindows", "LinkedIn", true, null),
            ("SpotifyAB.SpotifyMusic", "Spotify", false, null),
            ("Disney.37853FC22B2CE", "Disney+", true, null),
            ("AmazonVideo.PrimeVideo", "Prime Video", true, null),
            ("*Netflix*", "Netflix", false, null),
            ("BytedancePte.Ltd.TikTok", "TikTok", true, null),
            ("*Instagram*", "Instagram", true, null),
            ("Facebook.*", "Facebook", true, null),
            ("king.com.*", "King games (Candy Crush etc.)", true, null),
            ("*CandyCrush*", "Candy Crush", true, null),
            ("*BubbleWitch*", "Bubble Witch", true, null),
            ("*Twitter*", "X (Twitter)", true, null),
            ("*Duolingo*", "Duolingo", true, null),
        };

        public static List<BloatApp> Scan()
        {
            var r = Shell.PowerShell("Get-AppxPackage -AllUsers | Select-Object -ExpandProperty Name");
            if (!r.Ok || string.IsNullOrWhiteSpace(r.Output))
                r = Shell.PowerShell("Get-AppxPackage | Select-Object -ExpandProperty Name");

            var installed = r.Output
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0 && !s.Contains(' '))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var found = new List<BloatApp>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in Catalog)
            {
                var rx = WildcardToRegex(c.Pattern);
                var match = installed.FirstOrDefault(n => rx.IsMatch(n));
                if (match == null || !seen.Add(match)) continue;
                found.Add(new BloatApp
                {
                    Pattern = c.Pattern, Name = c.Name, Note = c.Note, Recommended = c.Rec,
                    PackageName = match, IsChecked = c.Rec
                });
            }

            if (OneDriveInstalled())
                found.Add(new BloatApp
                {
                    Pattern = "OneDrive", Name = "OneDrive", PackageName = "Microsoft.OneDrive (desktop app)",
                    Note = "Make sure your files are synced/downloaded before removing.", IsOneDrive = true
                });

            return found.OrderByDescending(a => a.Recommended).ThenBy(a => a.Name).ToList();
        }

        /// <summary>Removes the app for all users and stops it being reinstalled for new users. Returns null on success.</summary>
        public static string Remove(BloatApp app)
        {
            if (app.IsOneDrive) return RemoveOneDrive();

            string p = app.Pattern.Replace("'", "''");
            string script =
                $"Get-AppxPackage -AllUsers -Name '{p}' | ForEach-Object {{ Remove-AppxPackage -Package $_.PackageFullName -AllUsers -ErrorAction SilentlyContinue }}; " +
                $"Get-AppxPackage -Name '{p}' | Remove-AppxPackage -ErrorAction SilentlyContinue; " +
                $"Get-AppxProvisionedPackage -Online | Where-Object {{ $_.DisplayName -like '{p}' }} | ForEach-Object {{ Remove-AppxProvisionedPackage -Online -PackageName $_.PackageName -ErrorAction SilentlyContinue | Out-Null }}; " +
                $"if (Get-AppxPackage -Name '{p}') {{ 'ZENITH_STILL_INSTALLED' }}";
            var r = Shell.PowerShell(script);
            Log.Write($"Debloat {app.PackageName}: {(r.Output.Contains("ZENITH_STILL_INSTALLED") ? "partial" : "removed")}");
            return r.Output.Contains("ZENITH_STILL_INSTALLED") ? "Windows blocked removing this app (it may be a protected system app)." : null;
        }

        static bool OneDriveInstalled()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return File.Exists(Path.Combine(local, @"Microsoft\OneDrive\OneDrive.exe"))
                || File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft OneDrive\OneDrive.exe"))
                || File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft OneDrive\OneDrive.exe"));
        }

        static string RemoveOneDrive()
        {
            Shell.Run(Shell.Exe("taskkill.exe"), "/f /im OneDrive.exe");
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            foreach (var setup in new[] { Path.Combine(win, @"System32\OneDriveSetup.exe"), Path.Combine(win, @"SysWOW64\OneDriveSetup.exe") })
            {
                if (!File.Exists(setup)) continue;
                Shell.Run(setup, "/uninstall", 180_000);
                if (!OneDriveInstalled()) return null;
            }
            Shell.Run("winget", "uninstall --id Microsoft.OneDrive -e --silent --accept-source-agreements --disable-interactivity", 300_000);
            return OneDriveInstalled() ? "OneDrive couldn't be removed automatically. Uninstall it from Settings → Apps." : null;
        }

        static Regex WildcardToRegex(string pattern) =>
            new Regex("^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase);
    }
}
