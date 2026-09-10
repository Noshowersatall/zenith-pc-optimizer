using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Zenith.Core;

namespace Zenith.Views
{
    public partial class DashboardView : UserControl
    {
        const int Samples = 60;
        const double RingDiameter = 176, RingThickness = 12;

        readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        readonly DispatcherTimer _scoreTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        readonly List<double> _cpu = Enumerable.Repeat(0.0, Samples).ToList();
        double _shownScore, _targetScore;
        bool _refreshing;

        public DashboardView()
        {
            InitializeComponent();
            _timer.Tick += (s, e) => Tick();
            _scoreTimer.Tick += (s, e) => AnimateScore();
            Loaded += OnLoaded;
            Unloaded += (s, e) => _timer.Stop();

            InfoOs.Text = $"{SystemInfo.OsName}  {SystemInfo.OsVersion}";
            InfoCpu.Text = SystemInfo.CpuName;
            InfoCpu.ToolTip = SystemInfo.CpuName;
            InfoGpu.Text = SystemInfo.GpuName;
            InfoGpu.ToolTip = SystemInfo.GpuName;
            InfoRam.Text = $"{Format.Bytes((long)SystemInfo.TotalRam)} installed";
            InfoName.Text = SystemInfo.MachineName;
            CpuSub.Text = $"{SystemInfo.CoreCount} logical processors";
            DiskTitle.Text = $"System drive ({SystemInfo.SystemDrive.TrimEnd('\\')})";
        }

        async void OnLoaded(object sender, RoutedEventArgs e)
        {
            SystemInfo.CpuUsage(); // prime the counter
            _timer.Start();
            Tick();
            await RefreshHealth(false);
        }

        // ---------------- live stats ----------------

        void Tick()
        {
            double cpu = SystemInfo.CpuUsage();
            _cpu.RemoveAt(0);
            _cpu.Add(cpu);
            CpuValue.Text = $"{cpu:0}%";
            DrawSpark();

            var mem = SystemInfo.Memory();
            RamValue.Text = $"{mem.Percent:0}%";
            RamSub.Text = $"{Format.Bytes((long)mem.Used)} of {Format.Bytes((long)mem.Total)}";
            RamBar.Value = mem.Percent;

            var (free, total) = SystemInfo.Disk();
            double usedPct = total > 0 ? 100.0 * (total - free) / total : 0;
            DiskValue.Text = $"{usedPct:0}%";
            DiskSub.Text = $"{Format.Bytes(free)} free of {Format.Bytes(total)}";
            DiskBar.Value = usedPct;
            DiskBar.Foreground = (Brush)FindResource(usedPct > 90 ? "DangerBrush" : "AccentGradientH");

            var up = SystemInfo.Uptime;
            UptimeValue.Text = Format.Duration(up);
            UptimeHint.Text = up.TotalDays >= 3
                ? "It's been a while — a restart clears memory leaks and finishes pending updates."
                : "Looking fresh. Restarting every few days keeps things snappy.";
            UptimeHint.Foreground = (Brush)FindResource(up.TotalDays >= 3 ? "WarningBrush" : "SubTextBrush");
        }

        void SparkHost_SizeChanged(object sender, SizeChangedEventArgs e) => DrawSpark();

        void DrawSpark()
        {
            double w = SparkHost.ActualWidth, h = SparkHost.ActualHeight;
            if (w <= 0 || h <= 0) return;
            var line = new PointCollection(Samples);
            for (int i = 0; i < Samples; i++)
                line.Add(new Point(i * w / (Samples - 1), h - 2 - _cpu[i] / 100.0 * (h - 4)));
            var fill = new PointCollection(line) { new Point(w, h), new Point(0, h) };
            Spark.Points = line;
            SparkFill.Points = fill;
        }

        // ---------------- health score ----------------

        async Task RefreshHealth(bool forceScan)
        {
            if (_refreshing) return;
            _refreshing = true;
            HealthSpinner.Visibility = Visibility.Visible;
            long junk = 0;
            int startupOn = 0, startupTotal = 0;
            double diskFree = 100;
            try
            {
                await Task.Run(() =>
                {
                    foreach (var t in AppState.Tweaks) t.Refresh();
                    if (forceScan || (DateTime.Now - AppState.LastJunkScan).TotalSeconds > 120)
                        Cleaner.ScanAll(AppState.CleanTargets);
                    junk = AppState.CleanTargets.Where(c => c.Recommended).Sum(c => c.Size);
                    var entries = StartupManager.Load();
                    startupTotal = entries.Count;
                    startupOn = entries.Count(x => x.IsEnabled);
                    var (free, total) = SystemInfo.Disk();
                    diskFree = total > 0 ? 100.0 * free / total : 100;
                });
            }
            catch (Exception ex) { Log.Write("RefreshHealth: " + ex); }

            var rec = AppState.Tweaks.Where(t => t.Recommended).ToList();
            int applied = rec.Count(t => t.IsOn);
            JunkValue.Text = Format.Bytes(junk);
            StartupValue.Text = $"{startupOn} of {startupTotal} enabled";
            TweaksValue.Text = $"{applied} of {rec.Count}";

            int score = TweakCatalog.HealthScore(AppState.Tweaks, junk, startupOn, diskFree);
            string label = score >= 90 ? "Excellent" : score >= 70 ? "Good" : score >= 50 ? "Could be better" : "Needs attention";
            ScoreLabel.Text = label;
            SubGreeting.Text = score >= 90
                ? "Your PC is fully optimized. Nice."
                : $"Zenith found {rec.Count - applied} optimizations and {Format.Bytes(junk)} of junk to clean.";

            _targetScore = score;
            _scoreTimer.Start();
            HealthSpinner.Visibility = Visibility.Collapsed;
            _refreshing = false;
        }

        void AnimateScore()
        {
            double diff = _targetScore - _shownScore;
            if (Math.Abs(diff) < 0.5) { _shownScore = _targetScore; _scoreTimer.Stop(); }
            else _shownScore += diff * 0.12;
            ScoreText.Text = ((int)Math.Round(_shownScore)).ToString();

            double circumference = Math.PI * (RingDiameter - RingThickness) / RingThickness; // in stroke-thickness units
            double dash = circumference * _shownScore / 100.0;
            ScoreArc.StrokeDashArray = new DoubleCollection { Math.Max(dash, 0.001), 1000 };
            ScoreArc.Opacity = _shownScore < 1 ? 0 : 1;
        }

        // ---------------- actions ----------------

        async void Optimize_Click(object sender, RoutedEventArgs e)
        {
            var mw = MainWindow.Instance;
            var pending = AppState.Tweaks.Where(t => t.Recommended && !t.IsOn).ToList();
            var targets = AppState.CleanTargets.Where(c => c.Recommended).ToList();
            long junk = targets.Sum(c => c.Size);

            string what = pending.Count == 0
                ? $"All recommended optimizations are already applied. Zenith will clean {Format.Bytes(junk)} of junk files."
                : $"Zenith will apply {pending.Count} recommended optimization{(pending.Count == 1 ? "" : "s")} and clean {Format.Bytes(junk)} of junk files. Browsers should be closed for the best cleanup.";
            if (!await mw.Confirm("Optimize your PC?", what, "Optimize")) return;

            bool restart = false;
            await mw.RunBusy("Optimizing your PC", async report =>
            {
                int steps = Math.Max(1, pending.Count + targets.Count), i = 0, ok = 0, failed = 0;
                long freed = 0;
                foreach (var t in pending)
                {
                    report($"Applying · {t.Title}", (double)i++ / steps);
                    string err = await t.SetAsync(true);
                    if (err == null) { ok++; restart |= t.NeedsRestart; }
                    else failed++;
                }
                foreach (var c in targets)
                {
                    report($"Cleaning · {c.Name}", (double)i++ / steps);
                    freed += await Task.Run(() => Cleaner.Clean(c));
                }
                report("Finishing up…", 1);
                string summary = $"Applied {ok} optimization{(ok == 1 ? "" : "s")} and freed {Format.Bytes(freed)} of disk space.";
                if (failed > 0) summary += $" {failed} tweak{(failed == 1 ? "" : "s")} isn't supported on this PC.";
                if (restart) summary += " Restart your PC to finish.";
                return summary;
            });
            if (restart) mw.ShowRestartBanner();
            await RefreshHealth(false);
        }

        async void FreeRam_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            btn.IsEnabled = false;
            string msg = await Task.Run(() => Maintenance.FreeMemory());
            btn.IsEnabled = true;
            MainWindow.Instance.ShowToast(msg);
            Tick();
        }

        void Go_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string key) MainWindow.Instance.Navigate(key);
        }
    }
}
