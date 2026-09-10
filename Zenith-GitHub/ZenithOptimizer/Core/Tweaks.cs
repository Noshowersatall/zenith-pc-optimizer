using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Zenith.Core
{
    /// <summary>A registry value with its "optimized" and "Windows default" state. Off == null means delete the value.</summary>
    public sealed class RegVal
    {
        public RegVal(string path, string name, object on, object off) { Path = path; Name = name; On = on; Off = off; }
        public string Path { get; }
        public string Name { get; }
        public object On { get; }
        public object Off { get; }
    }

    public sealed class Tweak : Observable
    {
        bool _isOn, _isBusy;

        public string Id { get; init; }
        public string Page { get; init; }
        public string Group { get; init; }
        public string Glyph { get; init; } = "\uE713";
        public string Title { get; init; }
        public string Description { get; init; }
        public string Warning { get; init; }
        public bool Recommended { get; init; }
        public bool NeedsRestart { get; init; }
        public RegVal[] Values { get; init; } = Array.Empty<RegVal>();
        public Func<bool> CustomCheck { get; init; }
        public Action ExtraApply { get; init; }
        public Action ExtraRevert { get; init; }

        public bool HasWarning => !string.IsNullOrEmpty(Warning);

        public bool IsOn { get => _isOn; set => Set(ref _isOn, value); }
        public bool IsBusy { get => _isBusy; private set => Set(ref _isBusy, value); }

        public bool Check()
        {
            try
            {
                bool reg = Values.All(v => Reg.ValueEquals(Reg.Get(v.Path, v.Name), v.On));
                bool custom = CustomCheck?.Invoke() ?? true;
                return reg && custom;
            }
            catch { return false; }
        }

        public void Refresh() => IsOn = Check();

        void ApplyNow()
        {
            foreach (var v in Values) Reg.Set(v.Path, v.Name, v.On);
            ExtraApply?.Invoke();
            Log.Write("Applied tweak: " + Id);
        }

        void RevertNow()
        {
            foreach (var v in Values)
            {
                if (v.Off == null) Reg.Delete(v.Path, v.Name);
                else Reg.Set(v.Path, v.Name, v.Off);
            }
            ExtraRevert?.Invoke();
            Log.Write("Reverted tweak: " + Id);
        }

        /// <summary>Applies or reverts the tweak. Returns an error message, or null on success.</summary>
        public async Task<string> SetAsync(bool on)
        {
            if (IsBusy) return "Busy";
            IsBusy = true;
            string error = null;
            try
            {
                await Task.Run(() => { if (on) ApplyNow(); else RevertNow(); });
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Write($"Tweak {Id} failed: {ex}");
            }
            await Task.Run(Refresh);
            if (error == null && IsOn != on) error = "Windows didn't accept this change on this PC.";
            IsBusy = false;
            return error;
        }
    }

    public static class TweakCatalog
    {
        public const string PerformancePage = "Performance";
        public const string PrivacyPage = "Privacy";

        const string ADV = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        const string CDM = @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
        const string DESKTOP = @"HKCU\Control Panel\Desktop";
        const string MM = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
        const string MMGAMES = MM + @"\Tasks\Games";

        static RegVal R(string path, string name, object on, object off) => new RegVal(path, name, on, off);

        static readonly byte[] PerfMask = { 0x90, 0x12, 0x03, 0x80, 0x10, 0x00, 0x00, 0x00 };
        static readonly byte[] DefaultMask = { 0x9E, 0x1E, 0x07, 0x80, 0x12, 0x00, 0x00, 0x00 };

        public static List<Tweak> Build() => new List<Tweak>
        {
            // ================= PERFORMANCE — Power & CPU =================
            new Tweak
            {
                Id = "ultimate-plan", Page = PerformancePage, Group = "Power & CPU", Glyph = "\uE7E8",
                Title = "Ultimate Performance power plan",
                Description = "Activates Windows' hidden top-tier power plan, which removes power-saving latency. Falls back to High Performance where Ultimate isn't available.",
                Recommended = true,
                CustomCheck = Power.IsPerformancePlanActive,
                ExtraApply = Power.ActivatePerformancePlan,
                ExtraRevert = Power.ActivateBalanced
            },
            new Tweak
            {
                Id = "cpu-max", Page = PerformancePage, Group = "Power & CPU", Glyph = "\uEC4A",
                Title = "Maximum CPU performance",
                Description = "Keeps the processor at 100% minimum state and disables core parking on the active plan, so every core responds instantly.",
                Recommended = true,
                CustomCheck = () => Power.GetAc("sub_processor", "PROCTHROTTLEMIN") == 100,
                ExtraApply = () =>
                {
                    Power.SetBoth("sub_processor", "PROCTHROTTLEMIN", 100);
                    Power.SetBoth("sub_processor", "CPMINCORES", 100);
                    Power.Reapply();
                },
                ExtraRevert = () =>
                {
                    Power.SetBoth("sub_processor", "PROCTHROTTLEMIN", 5);
                    Power.SetBoth("sub_processor", "CPMINCORES", 10);
                    Power.Reapply();
                }
            },
            new Tweak
            {
                Id = "power-throttling", Page = PerformancePage, Group = "Power & CPU", Glyph = "\uE945",
                Title = "Disable power throttling",
                Description = "Stops Windows from slowing down background apps to save energy.",
                Recommended = true, NeedsRestart = true,
                Values = new[] { R(@"HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1, null) }
            },
            new Tweak
            {
                Id = "usb-pcie", Page = PerformancePage, Group = "Power & CPU", Glyph = "\uE772",
                Title = "Disable USB & PCIe power saving",
                Description = "Turns off USB selective suspend and PCIe link-state power management. Fixes input lag, audio crackle and device wake-up delays.",
                Recommended = true,
                CustomCheck = () => Power.GetAc(Power.SubUsb, Power.UsbSelectiveSuspend) == 0 && Power.GetAc(Power.SubPcie, Power.PcieAspm) == 0,
                ExtraApply = () =>
                {
                    Power.SetBoth(Power.SubUsb, Power.UsbSelectiveSuspend, 0);
                    Power.SetBoth(Power.SubPcie, Power.PcieAspm, 0);
                    Power.Reapply();
                },
                ExtraRevert = () =>
                {
                    Power.SetBoth(Power.SubUsb, Power.UsbSelectiveSuspend, 1);
                    Power.SetAcDc(Power.SubPcie, Power.PcieAspm, 1, 2);
                    Power.Reapply();
                }
            },
            new Tweak
            {
                Id = "hibernation", Page = PerformancePage, Group = "Power & CPU", Glyph = "\uE708",
                Title = "Disable hibernation & Fast Startup",
                Description = "Deletes hiberfil.sys (frees several GB) and makes every shutdown a clean boot, which avoids many driver glitches.",
                Recommended = true,
                Warning = "Laptops lose the Hibernate option.",
                CustomCheck = () => Reg.GetInt(@"HKLM\SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled") == 0,
                ExtraApply = () => Power.Hibernate(false),
                ExtraRevert = () => Power.Hibernate(true)
            },

            // ================= PERFORMANCE — System =================
            new Tweak
            {
                Id = "startup-delay", Page = PerformancePage, Group = "System", Glyph = "\uE81C",
                Title = "Remove startup app delay",
                Description = "Windows waits several seconds before launching startup apps. This makes your desktop ready sooner.",
                Recommended = true,
                Values = new[]
                {
                    R(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0, null),
                    R(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "WaitForIdleState", 0, null)
                }
            },
            new Tweak
            {
                Id = "ntfs-lastaccess", Page = PerformancePage, Group = "System", Glyph = "\uEDA2",
                Title = "Disable NTFS last-access timestamps",
                Description = "Stops Windows writing a timestamp every time a file is read, reducing disk writes.",
                Recommended = true,
                CustomCheck = () =>
                {
                    int? v = Reg.GetInt(@"HKLM\SYSTEM\CurrentControlSet\Control\FileSystem", "NtfsDisableLastAccessUpdate");
                    return v.HasValue && (v.Value & 1) == 1;
                },
                ExtraApply = () => Shell.Run(Shell.Exe("fsutil.exe"), "behavior set disablelastaccess 1"),
                ExtraRevert = () => Shell.Run(Shell.Exe("fsutil.exe"), "behavior set disablelastaccess 2")
            },
            new Tweak
            {
                Id = "edge-background", Page = PerformancePage, Group = "System", Glyph = "\uE774",
                Title = "Stop Edge running in the background",
                Description = "Disables Edge Startup Boost and background mode so it stops using RAM and CPU when closed.",
                Recommended = true,
                Warning = "Edge will show \"Managed by your organization\" because this uses a policy.",
                Values = new[]
                {
                    R(@"HKLM\SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled", 0, null),
                    R(@"HKLM\SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled", 0, null)
                }
            },
            new Tweak
            {
                Id = "background-apps", Page = PerformancePage, Group = "System", Glyph = "\uE71D",
                Title = "Block Store apps from running in background",
                Description = "Prevents Microsoft Store apps from using CPU, RAM and network while you aren't using them.",
                Warning = "Store apps (e.g. Phone Link, Mail) won't sync or notify while closed.",
                Values = new[]
                {
                    R(@"HKCU\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1, 0),
                    R(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsRunInBackground", 2, null)
                }
            },

            // ================= PERFORMANCE — Gaming =================
            new Tweak
            {
                Id = "game-mode", Page = PerformancePage, Group = "Gaming", Glyph = "\uE7FC",
                Title = "Game Mode",
                Description = "Prioritizes your game and pauses Windows Update installs and notifications while playing.",
                Recommended = true,
                Values = new[]
                {
                    R(@"HKCU\Software\Microsoft\GameBar", "AutoGameModeEnabled", 1, 0),
                    R(@"HKCU\Software\Microsoft\GameBar", "AllowAutoGameMode", 1, 0)
                }
            },
            new Tweak
            {
                Id = "hags", Page = PerformancePage, Group = "Gaming", Glyph = "\uE7F4",
                Title = "Hardware-accelerated GPU scheduling",
                Description = "Lets your graphics card manage its own memory, reducing latency. Requires a supported GPU and driver.",
                Recommended = true, NeedsRestart = true,
                Values = new[] { R(@"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2, 1) }
            },
            new Tweak
            {
                Id = "game-dvr", Page = PerformancePage, Group = "Gaming", Glyph = "\uE714",
                Title = "Disable background game recording",
                Description = "Turns off Game DVR, which constantly records gameplay in the background and costs FPS.",
                Recommended = true,
                Warning = "Xbox Game Bar clip capture won't work.",
                Values = new[]
                {
                    R(@"HKCU\System\GameConfigStore", "GameDVR_Enabled", 0, 1),
                    R(@"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0, 1),
                    R(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0, null)
                }
            },
            new Tweak
            {
                Id = "mmcss", Page = PerformancePage, Group = "Gaming", Glyph = "\uE9D9",
                Title = "Gaming & multimedia priority",
                Description = "Gives games more CPU and I/O priority and removes the network throttling Windows applies during media playback.",
                Recommended = true, NeedsRestart = true,
                Values = new[]
                {
                    R(MM, "SystemResponsiveness", 10, 20),
                    R(MM, "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), 10),
                    R(MMGAMES, "Priority", 6, 2),
                    R(MMGAMES, "Scheduling Category", "High", "Medium"),
                    R(MMGAMES, "SFIO Priority", "High", "Normal")
                }
            },
            new Tweak
            {
                Id = "mouse-accel", Page = PerformancePage, Group = "Gaming", Glyph = "\uE962",
                Title = "Disable mouse acceleration",
                Description = "Turns off \"Enhance pointer precision\" for 1:1 raw mouse movement — better aim and consistency.",
                Recommended = true,
                Values = new[]
                {
                    R(@"HKCU\Control Panel\Mouse", "MouseSpeed", "0", "1"),
                    R(@"HKCU\Control Panel\Mouse", "MouseThreshold1", "0", "6"),
                    R(@"HKCU\Control Panel\Mouse", "MouseThreshold2", "0", "10")
                },
                ExtraApply = () => Native.SetMouseAcceleration(false),
                ExtraRevert = () => Native.SetMouseAcceleration(true)
            },
            new Tweak
            {
                Id = "foreground-boost", Page = PerformancePage, Group = "Gaming", Glyph = "\uE945",
                Title = "Boost foreground app priority",
                Description = "Gives the app you're using (your game) longer, higher-priority CPU time slices than background tasks.",
                Values = new[] { R(@"HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl", "Win32PrioritySeparation", 38, 2) }
            },

            // ================= PERFORMANCE — Visual effects =================
            new Tweak
            {
                Id = "animations", Page = PerformancePage, Group = "Visual effects", Glyph = "\uE768",
                Title = "Disable animations",
                Description = "Removes window, taskbar and control animations so everything opens instantly.",
                Recommended = true,
                Values = new[]
                {
                    R(@"HKCU\Control Panel\Desktop\WindowMetrics", "MinAnimate", "0", "1"),
                    R(ADV, "TaskbarAnimations", 0, 1)
                },
                ExtraApply = () => Native.SetAnimations(false),
                ExtraRevert = () => Native.SetAnimations(true)
            },
            new Tweak
            {
                Id = "menu-delay", Page = PerformancePage, Group = "Visual effects", Glyph = "\uE700",
                Title = "Instant menus",
                Description = "Removes the 400 ms delay before sub-menus open.",
                Recommended = true,
                Values = new[] { R(DESKTOP, "MenuShowDelay", "0", "400") },
                ExtraApply = () => Native.SetMenuDelay(0),
                ExtraRevert = () => Native.SetMenuDelay(400)
            },
            new Tweak
            {
                Id = "transparency", Page = PerformancePage, Group = "Visual effects", Glyph = "\uE771",
                Title = "Disable transparency effects",
                Description = "Turns off the acrylic/mica blur in Start, taskbar and windows. Saves GPU work on lower-end PCs.",
                Values = new[] { R(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0, 1) },
                ExtraApply = () => Native.BroadcastSettingChange("ImmersiveColorSet"),
                ExtraRevert = () => Native.BroadcastSettingChange("ImmersiveColorSet")
            },
            new Tweak
            {
                Id = "visualfx", Page = PerformancePage, Group = "Visual effects", Glyph = "\uE771",
                Title = "Performance visual style",
                Description = "Applies Windows' \"Adjust for best performance\" effects (shadows, fades, live dragging off) while keeping smooth fonts and thumbnails.",
                NeedsRestart = true,
                Values = new[]
                {
                    R(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 3, 0),
                    R(DESKTOP, "UserPreferencesMask", PerfMask, DefaultMask),
                    R(DESKTOP, "DragFullWindows", "0", "1"),
                    R(ADV, "ListviewAlphaSelect", 0, 1),
                    R(ADV, "ListviewShadow", 0, 1)
                }
            },

            // ================= PERFORMANCE — Advanced =================
            new Tweak
            {
                Id = "vbs-hvci", Page = PerformancePage, Group = "Advanced — use with care", Glyph = "\uEA18",
                Title = "Disable Memory Integrity (VBS/HVCI)",
                Description = "Can raise gaming FPS by 5–10% on many CPUs. Microsoft itself suggests this for gaming performance.",
                Warning = "Lowers protection against kernel-level malware. Keep it on if you handle sensitive data.",
                NeedsRestart = true,
                Values = new[] { R(@"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled", 0, 1) }
            },
            new Tweak
            {
                Id = "sysmain", Page = PerformancePage, Group = "Advanced — use with care", Glyph = "\uE9D9",
                Title = "Disable SysMain (Superfetch)",
                Description = "Stops Windows preloading apps into RAM. Helps PCs with a hard drive or little RAM; on SSD PCs the gain is small.",
                CustomCheck = () => Svc.IsDisabledOrMissing("SysMain"),
                ExtraApply = () => Svc.Disable("SysMain"),
                ExtraRevert = () => Svc.Enable("SysMain", "auto")
            },
            new Tweak
            {
                Id = "search-index", Page = PerformancePage, Group = "Advanced — use with care", Glyph = "\uE721",
                Title = "Disable Windows Search indexing",
                Description = "Stops the background indexer from scanning your files. Frees CPU and disk activity.",
                Warning = "Searching files in Start and Explorer becomes slower.",
                CustomCheck = () => Svc.IsDisabledOrMissing("WSearch"),
                ExtraApply = () => Svc.Disable("WSearch"),
                ExtraRevert = () => Svc.Enable("WSearch", "delayed-auto")
            },

            // ================= PRIVACY — Data collection =================
            new Tweak
            {
                Id = "telemetry", Page = PrivacyPage, Group = "Data collection", Glyph = "\uEA18",
                Title = "Disable telemetry",
                Description = "Reduces diagnostic data sent to Microsoft to the minimum and stops the Connected User Experiences service.",
                Recommended = true,
                Values = new[]
                {
                    R(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0, null),
                    R(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "DoNotShowFeedbackNotifications", 1, null)
                },
                CustomCheck = () => Svc.IsDisabledOrMissing("DiagTrack"),
                ExtraApply = () => { Svc.Disable("DiagTrack"); Svc.Disable("dmwappushservice"); },
                ExtraRevert = () => { Svc.Enable("DiagTrack", "auto"); Svc.Enable("dmwappushservice", "demand"); }
            },
            new Tweak
            {
                Id = "tailored", Page = PrivacyPage, Group = "Data collection", Glyph = "\uE716",
                Title = "Disable tailored experiences & feedback prompts",
                Description = "Stops Microsoft using your diagnostic data for personalized tips and ads, and stops \"How likely are you to recommend\" pop-ups.",
                Recommended = true,
                Values = new[]
                {
                    R(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0, 1),
                    R(@"HKCU\Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0, null)
                }
            },
            new Tweak
            {
                Id = "activity", Page = PrivacyPage, Group = "Data collection", Glyph = "\uE81C",
                Title = "Disable activity history",
                Description = "Stops Windows recording the apps, files and sites you open.",
                Recommended = true,
                Values = new[]
                {
                    R(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0, null),
                    R(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0, null),
                    R(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0, null)
                }
            },
            new Tweak
            {
                Id = "error-reporting", Page = PrivacyPage, Group = "Data collection", Glyph = "\uE7BA",
                Title = "Disable Windows Error Reporting",
                Description = "Stops crash reports being collected and uploaded in the background.",
                Recommended = true,
                Values = new[] { R(@"HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting", "Disabled", 1, 0) }
            },

            // ================= PRIVACY — Ads & suggestions =================
            new Tweak
            {
                Id = "ad-id", Page = PrivacyPage, Group = "Ads & suggestions", Glyph = "\uE719",
                Title = "Disable advertising ID",
                Description = "Prevents apps from using your advertising ID to track you across apps.",
                Recommended = true,
                Values = new[]
                {
                    R(@"HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0, 1),
                    R(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy", 1, null)
                }
            },
            new Tweak
            {
                Id = "suggestions", Page = PrivacyPage, Group = "Ads & suggestions", Glyph = "\uE946",
                Title = "Disable tips, suggestions & Start menu ads",
                Description = "Removes promoted apps, lock-screen tips, \"suggested\" content in Settings and silent app installs like Candy Crush.",
                Recommended = true,
                Values = new[]
                {
                    R(CDM, "SubscribedContent-338387Enabled", 0, 1),
                    R(CDM, "SubscribedContent-338388Enabled", 0, 1),
                    R(CDM, "SubscribedContent-338389Enabled", 0, 1),
                    R(CDM, "SubscribedContent-338393Enabled", 0, 1),
                    R(CDM, "SubscribedContent-353694Enabled", 0, 1),
                    R(CDM, "SubscribedContent-353696Enabled", 0, 1),
                    R(CDM, "SubscribedContent-310093Enabled", 0, 1),
                    R(CDM, "RotatingLockScreenOverlayEnabled", 0, 1),
                    R(CDM, "SystemPaneSuggestionsEnabled", 0, 1),
                    R(CDM, "SilentInstalledAppsEnabled", 0, 1),
                    R(CDM, "SoftLandingEnabled", 0, 1),
                    R(ADV, "Start_IrisRecommendations", 0, 1),
                    R(ADV, "ShowSyncProviderNotifications", 0, 1),
                    R(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1, null)
                }
            },

            // ================= PRIVACY — Search, AI & widgets =================
            new Tweak
            {
                Id = "bing-search", Page = PrivacyPage, Group = "Search, AI & widgets", Glyph = "\uE721",
                Title = "Remove Bing web results from Start search",
                Description = "Start search only shows your apps, files and settings — faster, and your typing isn't sent to Bing.",
                Recommended = true, NeedsRestart = true,
                Values = new[]
                {
                    R(@"HKCU\Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1, null),
                    R(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0, 1)
                }
            },
            new Tweak
            {
                Id = "copilot", Page = PrivacyPage, Group = "Search, AI & widgets", Glyph = "\uE99A",
                Title = "Turn off Copilot",
                Description = "Disables Windows Copilot and removes its taskbar button. You can also uninstall the Copilot app on the Debloat page.",
                Recommended = true, NeedsRestart = true,
                Values = new[]
                {
                    R(@"HKCU\Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1, null),
                    R(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1, null),
                    R(ADV, "ShowCopilotButton", 0, null)
                }
            },
            new Tweak
            {
                Id = "recall", Page = PrivacyPage, Group = "Search, AI & widgets", Glyph = "\uE722",
                Title = "Disable Recall snapshots",
                Description = "Prevents Windows Recall from taking and analyzing screenshots of your activity (Copilot+ PCs).",
                Recommended = true, NeedsRestart = true,
                Values = new[]
                {
                    R(@"HKCU\Software\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1, null),
                    R(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "DisableAIDataAnalysis", 1, null),
                    R(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsAI", "AllowRecallEnablement", 0, null)
                }
            },
            new Tweak
            {
                Id = "widgets", Page = PrivacyPage, Group = "Search, AI & widgets", Glyph = "\uE71D",
                Title = "Disable Widgets & news feed",
                Description = "Removes the Widgets board and its background news process from the taskbar.",
                Recommended = true, NeedsRestart = true,
                Values = new[] { R(@"HKLM\SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0, null) }
            },

            // ================= PRIVACY — Network =================
            new Tweak
            {
                Id = "delivery-optimization", Page = PrivacyPage, Group = "Network", Glyph = "\uE753",
                Title = "Stop sharing updates with other PCs",
                Description = "Disables peer-to-peer Delivery Optimization so your upload bandwidth isn't used to send updates to other computers.",
                Recommended = true,
                Values = new[] { R(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0, null) }
            },
        };

        public static int HealthScore(IEnumerable<Tweak> tweaks, long junkBytes, int startupEnabled, double diskFreePercent)
        {
            var rec = tweaks.Where(t => t.Recommended).ToList();
            double s = rec.Count == 0 ? 45 : 45.0 * rec.Count(t => t.IsOn) / rec.Count;
            const long MB = 1024L * 1024;
            s += junkBytes < 300 * MB ? 20 : junkBytes < 1024 * MB ? 15 : junkBytes < 3072 * MB ? 9 : 4;
            s += startupEnabled <= 4 ? 20 : startupEnabled <= 8 ? 14 : startupEnabled <= 12 ? 8 : 3;
            s += diskFreePercent >= 20 ? 15 : diskFreePercent >= 10 ? 9 : 3;
            return (int)Math.Round(Math.Clamp(s, 0, 100));
        }
    }
}
