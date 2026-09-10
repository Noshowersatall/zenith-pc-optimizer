using System;
using System.Drawing;
using WinForms = System.Windows.Forms;

namespace Zenith.UI
{
    /// <summary>System tray icon so Game Booster keeps working when the window is closed.</summary>
    public sealed class Tray : IDisposable
    {
        readonly WinForms.NotifyIcon _icon;

        public Tray(Action open, Action exit)
        {
            Icon icon = null;
            try { icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath); } catch { }
            _icon = new WinForms.NotifyIcon
            {
                Icon = icon ?? SystemIcons.Application,
                Text = "Zenith Optimizer",
                Visible = true
            };
            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("Open Zenith", null, (s, e) => open());
            menu.Items.Add("Exit", null, (s, e) => exit());
            _icon.ContextMenuStrip = menu;
            _icon.DoubleClick += (s, e) => open();
        }

        public void Notify(string title, string text) =>
            _icon.ShowBalloonTip(4000, title, text, WinForms.ToolTipIcon.None);

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
    }
}
