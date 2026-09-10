using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Zenith.Core;
using Zenith.Views;

namespace Zenith
{
    public partial class MainWindow : Window
    {
        public static MainWindow Instance { get; private set; }

        readonly Dictionary<string, UserControl> _pages = new Dictionary<string, UserControl>();
        readonly DispatcherTimer _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
        TaskCompletionSource<bool> _confirmTcs, _busyTcs;
        Zenith.UI.Tray _tray;
        bool _exiting, _trayHintShown;

        public MainWindow()
        {
            Instance = this;
            InitializeComponent();
            MachineText.Text = $"{SystemInfo.MachineName} · {SystemInfo.OsName.Replace("Windows ", "Win ")}";
            string first = SystemInfo.UserFirstName;
            HelloText.Text = $"Hello, {first}";
            AvatarText.Text = first.Substring(0, 1).ToUpperInvariant();
            DateText.Text = DateTime.Now.ToString("dddd, MMMM d");

            SourceInitialized += OnSourceInitialized;
            StateChanged += OnStateChanged;
            _toastTimer.Tick += (s, e) => { _toastTimer.Stop(); Fade(Toast, 0); };
            Navigate("Dashboard");

            // Tray icon + Game Booster live monitor
            _tray = new Zenith.UI.Tray(ShowFromTray, ExitApp);
            BoostMonitor.GameStarted += name => Dispatcher.Invoke(() =>
            {
                _tray.Notify("Game Booster", $"{name} is boosted — RAM freed and background apps lowered.");
                if (IsVisible) ShowToast($"{name} started · boost active");
            });
            BoostMonitor.GameStopped += name => Dispatcher.Invoke(() => { if (IsVisible) ShowToast($"{name} closed · boost ended"); });
            if (SettingsStore.Current.AutoBoost && SettingsStore.Current.Games.Count > 0) BoostMonitor.Start();

            Closing += OnClosing;
            Closed += (s, e) => { BoostMonitor.Stop(); _tray?.Dispose(); };
        }

        void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var cfg = SettingsStore.Current;
            if (_exiting || !cfg.TrayOnClose || !BoostMonitor.IsRunning) return;
            // keep Game Booster running in the tray
            e.Cancel = true;
            Hide();
            if (!_trayHintShown)
            {
                _tray.Notify("Zenith is still running", "Game Booster keeps watching for your games. Right-click the tray icon to exit.");
                _trayHintShown = true;
            }
        }

        public void ShowFromTray()
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }

        public void ExitApp()
        {
            _exiting = true;
            Close();
        }

        void OnSourceInitialized(object sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            int round = 2;   // DWMWCP_ROUND — Windows 11 rounded corners
            Native.DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
            int dark = 1;    // DWMWA_USE_IMMERSIVE_DARK_MODE
            Native.DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
            int border = 0x00262422; // DWMWA_BORDER_COLOR (COLORREF 0x00BBGGRR) — subtle dark border
            Native.DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int));
        }

        void OnStateChanged(object sender, EventArgs e)
        {
            // A maximized WindowChrome window overhangs the screen by the resize border
            Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            MaxButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        }

        // ---------------- Navigation ----------------

        void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (PageHost == null) return; // fires during InitializeComponent
            if (sender is RadioButton rb && rb.Content is string key) Navigate(key);
        }

        public void Navigate(string key)
        {
            if (!_pages.TryGetValue(key, out var page))
            {
                page = key switch
                {
                    "Dashboard" => new DashboardView(),
                    "Cleaner" => new CleanerView(),
                    "Startup Apps" => new StartupView(),
                    "Performance" => new TweaksView(TweakCatalog.PerformancePage, "Performance",
                        "Squeeze every bit of speed out of your hardware. Every toggle shows its real current state and can be switched back anytime."),
                    "Privacy" => new TweaksView(TweakCatalog.PrivacyPage, "Privacy",
                        "Cut telemetry, ads and AI features that run in the background and phone home."),
                    "Debloat" => new DebloatView(),
                    "Tools" => new ToolsView(),
                    "Game Booster" => new GameBoosterView(),
                    "Uninstaller" => new UninstallerView(),
                    "Disk Space" => new DiskView(),
                    "Processes" => new ProcessesView(),
                    "Benchmark" => new BenchmarkView(),
                    "Network" => new NetworkView(),
                    _ => null
                };
                if (page == null) return;
                _pages[key] = page;
            }

            // keep the sidebar in sync for programmatic navigation
            foreach (var rb in Nav.Children.OfType<RadioButton>())
                if (rb.Content as string == key && rb.IsChecked != true) { rb.IsChecked = true; return; }

            if (PageHost.Content == page) return;
            PageHost.Content = page;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            PageHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
            ((TranslateTransform)PageHost.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
        }

        // ---------------- Toast ----------------

        public void ShowToast(string message, bool error = false)
        {
            ToastText.Text = message;
            ToastIcon.Text = error ? "\uE7BA" : "\uE73E";
            ToastIcon.Foreground = (Brush)FindResource(error ? "DangerBrush" : "SuccessBrush");
            _toastTimer.Stop();
            Fade(Toast, 1);
            _toastTimer.Interval = TimeSpan.FromSeconds(error ? 6 : 3.5);
            _toastTimer.Start();
        }

        static void Fade(UIElement el, double to) =>
            el.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(220)));

        // ---------------- Restart banner ----------------

        public void ShowRestartBanner() => RestartBanner.Visibility = Visibility.Visible;

        void RestartLater_Click(object sender, RoutedEventArgs e) => RestartBanner.Visibility = Visibility.Collapsed;

        async void RestartNow_Click(object sender, RoutedEventArgs e)
        {
            if (!await Confirm("Restart now?", "Save any open work first. Your PC will restart in 5 seconds.", "Restart", "\uE777")) return;
            Shell.Run(Shell.Exe("shutdown.exe"), "/r /t 5 /c \"Zenith Optimizer: restarting to apply optimizations\"");
        }

        // ---------------- Confirm dialog ----------------

        public Task<bool> Confirm(string title, string message, string okText = "Continue", string glyph = "\uE945")
        {
            ConfirmTitle.Text = title;
            ConfirmText.Text = message;
            ConfirmOk.Content = okText;
            ConfirmGlyph.Text = glyph;
            _confirmTcs = new TaskCompletionSource<bool>();
            ShowOverlay(ConfirmOverlay);
            return _confirmTcs.Task;
        }

        void ConfirmOk_Click(object sender, RoutedEventArgs e) { HideOverlay(ConfirmOverlay); _confirmTcs?.TrySetResult(true); }
        void ConfirmCancel_Click(object sender, RoutedEventArgs e) { HideOverlay(ConfirmOverlay); _confirmTcs?.TrySetResult(false); }

        // ---------------- Busy overlay ----------------

        /// <summary>Shows a progress dialog while work runs. work receives report(step, fraction 0..1) and returns a summary.</summary>
        public async Task RunBusy(string title, Func<Action<string, double>, Task<string>> work)
        {
            BusyTitle.Text = title;
            BusyStep.Text = "Starting…";
            BusyProgress.Value = 0;
            BusyDone.Visibility = Visibility.Collapsed;
            BusySpinner.Visibility = Visibility.Visible;
            BusyCheck.Visibility = Visibility.Collapsed;
            ShowOverlay(BusyOverlay);

            void Report(string step, double fraction) => Dispatcher.Invoke(() =>
            {
                BusyStep.Text = step;
                BusyProgress.Value = Math.Clamp(fraction, 0, 1) * 100;
            });

            string summary;
            try { summary = await work(Report); }
            catch (Exception ex)
            {
                Log.Write("RunBusy: " + ex);
                summary = "Something went wrong: " + ex.Message;
            }

            BusyTitle.Text = "All done";
            BusyStep.Text = summary;
            BusyProgress.Value = 100;
            BusySpinner.Visibility = Visibility.Collapsed;
            BusyCheck.Visibility = Visibility.Visible;
            BusyDone.Visibility = Visibility.Visible;

            _busyTcs = new TaskCompletionSource<bool>();
            await _busyTcs.Task;
            HideOverlay(BusyOverlay);
        }

        void BusyDone_Click(object sender, RoutedEventArgs e) => _busyTcs?.TrySetResult(true);

        static void ShowOverlay(FrameworkElement overlay)
        {
            overlay.Opacity = 0;
            overlay.Visibility = Visibility.Visible;
            overlay.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        }

        static void HideOverlay(FrameworkElement overlay)
        {
            overlay.BeginAnimation(OpacityProperty, null);
            overlay.Visibility = Visibility.Collapsed;
        }

        // ---------------- Caption buttons ----------------

        void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        void Max_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
