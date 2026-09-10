using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Zenith.Core;

namespace Zenith.Views
{
    public sealed class Metric
    {
        public string Title { get; init; }
        public string Unit { get; init; }
        public string LatestText { get; init; }
        public string BaselineText { get; init; }
        public bool HasDelta { get; init; }
        public string DeltaText { get; init; }
        public Brush DeltaForeground { get; init; }
        public Brush DeltaBackground { get; init; }
    }

    public partial class BenchmarkView : UserControl
    {
        public BenchmarkView()
        {
            InitializeComponent();
            Loaded += (s, e) => Render();
        }

        void Render()
        {
            var runs = SettingsStore.Current.Benchmarks;
            var latest = runs.LastOrDefault();
            var baseline = runs.Count > 1 ? runs.First() : null;
            ResetButton.IsEnabled = runs.Count > 1;

            MetricList.ItemsSource = new List<Metric>
            {
                Make("CPU · single core", "M ops/s", latest?.CpuSingle, baseline?.CpuSingle, true, "0"),
                Make("CPU · all cores", "M ops/s", latest?.CpuMulti, baseline?.CpuMulti, true, "0"),
                Make("Memory speed", "GB/s", latest?.RamGBs, baseline?.RamGBs, true, "0.0"),
                Make("Disk read", "MB/s", latest?.DiskReadMBs, baseline?.DiskReadMBs, true, "0"),
                Make("Disk write", "MB/s", latest?.DiskWriteMBs, baseline?.DiskWriteMBs, true, "0"),
                Make("Disk 4K random read", "IOPS", latest?.Disk4kIops, baseline?.Disk4kIops, true, "0"),
                Make("Last boot time", "seconds", latest?.BootSeconds, baseline?.BootSeconds, false, "0.0"),
            };

            HistoryLabel.Visibility = runs.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            HistoryText.Text = string.Join("\n", runs.AsEnumerable().Reverse().Take(8).Select((r, i) =>
                $"{r.Date.ToString("MMM d, yyyy  h:mm tt", CultureInfo.InvariantCulture)}   ·   CPU {r.CpuMulti:0}  ·  RAM {r.RamGBs:0.0} GB/s  ·  Disk {r.DiskReadMBs:0} MB/s" +
                (i == runs.Count - 1 && runs.Count > 1 ? "   (baseline)" : "")));
        }

        Metric Make(string title, string unit, double? latest, double? baseline, bool higherIsBetter, string fmt)
        {
            bool delta = latest.HasValue && baseline.HasValue && baseline.Value > 0;
            double pct = delta ? (latest.Value - baseline.Value) / baseline.Value * 100 : 0;
            bool better = higherIsBetter ? pct >= 0 : pct <= 0;
            bool flat = Math.Abs(pct) < 2;
            string brush = flat ? "SubTextBrush" : better ? "SuccessBrush" : "DangerBrush";
            string tint = flat ? "TrackBrush" : better ? "SuccessTintBrush" : "WarningTintBrush";
            return new Metric
            {
                Title = title,
                Unit = unit,
                LatestText = latest.HasValue ? latest.Value.ToString(fmt, CultureInfo.InvariantCulture) : "—",
                BaselineText = baseline.HasValue ? $"Baseline: {baseline.Value.ToString(fmt, CultureInfo.InvariantCulture)} {unit}"
                    : latest.HasValue ? "This is your baseline" : "Not measured yet",
                HasDelta = delta,
                DeltaText = flat ? "≈ same" : $"{(pct > 0 ? "+" : "")}{pct:0}%",
                DeltaForeground = (Brush)FindResource(brush),
                DeltaBackground = (Brush)FindResource(tint)
            };
        }

        async void Run_Click(object sender, RoutedEventArgs e)
        {
            var mw = MainWindow.Instance;
            BenchResult result = null;
            await mw.RunBusy("Running benchmark", async report =>
            {
                result = await Task.Run(() => Benchmark.Run(report));
                return $"CPU {result.CpuMulti:0} M ops/s  ·  RAM {result.RamGBs:0.0} GB/s  ·  Disk {result.DiskReadMBs:0} MB/s read.";
            });
            if (result == null) return;
            SettingsStore.Current.Benchmarks.Add(result);
            SettingsStore.Save();
            Render();
        }

        void Reset_Click(object sender, RoutedEventArgs e)
        {
            var runs = SettingsStore.Current.Benchmarks;
            if (runs.Count == 0) return;
            var last = runs.Last();
            runs.Clear();
            runs.Add(last);
            SettingsStore.Save();
            Render();
            MainWindow.Instance.ShowToast("Latest result is now your baseline.");
        }
    }
}
