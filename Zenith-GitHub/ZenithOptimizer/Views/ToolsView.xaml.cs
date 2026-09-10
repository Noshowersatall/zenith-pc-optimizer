using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Zenith.Core;

namespace Zenith.Views
{
    public sealed class ToolItem
    {
        public string Id { get; init; }
        public string Glyph { get; init; }
        public string Title { get; init; }
        public string Description { get; init; }
        public string Action { get; init; } = "Run";
        public string Hint { get; init; } = "";
    }

    public partial class ToolsView : UserControl
    {
        public ToolsView()
        {
            InitializeComponent();
            ToolList.ItemsSource = new List<ToolItem>
            {
                new ToolItem { Id = "ram", Glyph = "\uE9D9", Title = "Free up memory",
                    Description = "Empties the standby list and trims idle app memory. Handy right before launching a game.", Hint = "Instant" },
                new ToolItem { Id = "dns", Glyph = "\uE774", Title = "Flush DNS cache",
                    Description = "Clears cached website addresses. Fixes sites that won't load after network changes.", Hint = "Instant" },
                new ToolItem { Id = "repair", Glyph = "\uE90F", Title = "Repair Windows files",
                    Description = "Runs DISM RestoreHealth then System File Checker to find and fix corrupted system files.", Hint = "10–30 min", Action = "Open" },
                new ToolItem { Id = "drives", Glyph = "\uEDA2", Title = "Optimize drives",
                    Description = "TRIMs SSDs and defragments hard drives — the right action for each drive type.", Hint = "A few minutes", Action = "Open" },
                new ToolItem { Id = "components", Glyph = "\uE74D", Title = "Clean up Windows Update leftovers",
                    Description = "Removes superseded update components from WinSxS. Frees GB of space; old updates can no longer be uninstalled.", Hint = "5–15 min", Action = "Open" },
                new ToolItem { Id = "network", Glyph = "\uE701", Title = "Reset network stack",
                    Description = "Resets Winsock and TCP/IP settings. Fixes stubborn connection problems.", Hint = "Needs restart" },
                new ToolItem { Id = "explorer", Glyph = "\uE72C", Title = "Restart Explorer",
                    Description = "Restarts the taskbar and desktop. Applies some tweaks without signing out.", Hint = "Instant" },
                new ToolItem { Id = "restore", Glyph = "\uE81C", Title = "Create restore point",
                    Description = "Saves a System Restore snapshot you can roll back to from Windows recovery.", Hint = "About a minute", Action = "Create" },
                new ToolItem { Id = "log", Glyph = "\uE8A5", Title = "Activity log",
                    Description = "Every change Zenith makes is written to a log file on your PC.", Action = "Open" },
            };
        }

        async void Run_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string id) return;
            var mw = MainWindow.Instance;
            btn.IsEnabled = false;
            try
            {
                switch (id)
                {
                    case "ram": mw.ShowToast(await Task.Run(() => Maintenance.FreeMemory())); break;
                    case "dns": mw.ShowToast(await Task.Run(() => Maintenance.FlushDns())); break;
                    case "repair": Maintenance.RepairSystem(); mw.ShowToast("Repair started in a new window."); break;
                    case "drives": Maintenance.OptimizeDrives(); mw.ShowToast("Drive optimization started in a new window."); break;
                    case "components":
                        if (await mw.Confirm("Clean up update leftovers?", "After this, currently installed Windows updates can't be uninstalled. It's safe and frees a lot of space.", "Start"))
                        {
                            Maintenance.ComponentCleanup();
                            mw.ShowToast("Cleanup started in a new window.");
                        }
                        break;
                    case "network":
                        if (await mw.Confirm("Reset network stack?", "Your connection may drop briefly. VPN adapters and custom network settings may need to be set up again.", "Reset"))
                        {
                            mw.ShowToast(await Task.Run(() => Maintenance.ResetNetwork()));
                            mw.ShowRestartBanner();
                        }
                        break;
                    case "explorer": mw.ShowToast(await Task.Run(() => Maintenance.RestartExplorer())); break;
                    case "restore":
                        mw.ShowToast("Creating restore point…");
                        mw.ShowToast(await Task.Run(() => Maintenance.CreateRestorePoint()));
                        break;
                    case "log": Maintenance.OpenLog(); break;
                }
            }
            catch (Exception ex)
            {
                Log.Write($"Tool {id}: {ex}");
                mw.ShowToast(ex.Message, true);
            }
            finally { btn.IsEnabled = true; }
        }
    }
}
