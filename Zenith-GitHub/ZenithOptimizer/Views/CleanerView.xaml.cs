using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Zenith.Core;

namespace Zenith.Views
{
    public partial class CleanerView : UserControl
    {
        bool _busy;

        public CleanerView()
        {
            InitializeComponent();
            TargetList.ItemsSource = AppState.CleanTargets;
            Loaded += async (s, e) =>
            {
                if ((DateTime.Now - AppState.LastJunkScan).TotalSeconds > 60) await ScanAsync();
                else UpdateTotal();
            };
        }

        async Task ScanAsync()
        {
            if (_busy) return;
            SetBusy(true, "Scanning your PC…");
            await Task.Run(() => Cleaner.ScanAll(AppState.CleanTargets));
            SetBusy(false, null);
            UpdateTotal();
        }

        void SetBusy(bool busy, string text)
        {
            _busy = busy;
            ScanSpinner.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            SelectLinks.Visibility = busy ? Visibility.Collapsed : Visibility.Visible;
            ScanButton.IsEnabled = CleanButton.IsEnabled = !busy;
            if (text != null) TotalSub.Text = text;
        }

        void UpdateTotal()
        {
            var selected = AppState.CleanTargets.Where(t => t.IsChecked).ToList();
            long total = selected.Sum(t => t.Size);
            long all = AppState.CleanTargets.Sum(t => t.Size);
            TotalText.Text = Format.Bytes(total);
            TotalSub.Text = $"{selected.Count} of {AppState.CleanTargets.Count} categories selected · {Format.Bytes(all)} found in total";
            ScanSpinner.Visibility = Visibility.Collapsed;
            SelectLinks.Visibility = Visibility.Visible;
        }

        async void Scan_Click(object sender, RoutedEventArgs e) => await ScanAsync();

        void Item_Click(object sender, RoutedEventArgs e) => UpdateTotal();

        void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var t in AppState.CleanTargets) t.IsChecked = true;
            UpdateTotal();
        }

        void SelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var t in AppState.CleanTargets) t.IsChecked = false;
            UpdateTotal();
        }

        async void Clean_Click(object sender, RoutedEventArgs e)
        {
            var selected = AppState.CleanTargets.Where(t => t.IsChecked).ToList();
            if (selected.Count == 0) { MainWindow.Instance.ShowToast("Select at least one category to clean.", true); return; }

            SetBusy(true, "Cleaning…");
            long freed = 0;
            for (int i = 0; i < selected.Count; i++)
            {
                var t = selected[i];
                TotalSub.Text = $"Cleaning {t.Name}…  ({i + 1}/{selected.Count})";
                freed += await Task.Run(() => Cleaner.Clean(t));
            }
            SetBusy(false, null);
            UpdateTotal();
            MainWindow.Instance.ShowToast($"Freed {Format.Bytes(freed)} of disk space.");
        }
    }
}
