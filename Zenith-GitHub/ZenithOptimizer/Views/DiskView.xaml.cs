using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Zenith.Core;

namespace Zenith.Views
{
    public sealed class DriveCard
    {
        public string Root { get; init; }
        public string Label { get; init; }
        public double UsedPercent { get; init; }
        public string FreeText { get; init; }
    }

    public partial class DiskView : UserControl
    {
        DiskScan _scan;
        string _current;
        CancellationTokenSource _cts;

        public DiskView()
        {
            InitializeComponent();
            Loaded += (s, e) => LoadDrives();
        }

        void LoadDrives()
        {
            DriveList.ItemsSource = DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .Select(d => new DriveCard
                {
                    Root = d.RootDirectory.FullName,
                    Label = $"{(string.IsNullOrWhiteSpace(d.VolumeLabel) ? "Local Disk" : d.VolumeLabel)} ({d.Name.TrimEnd('\\')})",
                    UsedPercent = d.TotalSize > 0 ? 100.0 * (d.TotalSize - d.AvailableFreeSpace) / d.TotalSize : 0,
                    FreeText = $"{Format.Bytes(d.AvailableFreeSpace)} free of {Format.Bytes(d.TotalSize)}  ·  click to scan"
                }).ToList();
        }

        async void Drive_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not DriveCard drive) return;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            ScanCard.Visibility = Visibility.Visible;
            Toolbar.Visibility = Visibility.Collapsed;
            ItemList.ItemsSource = null;
            ScanTitle.Text = $"Scanning {drive.Label}…";
            ScanText.Text = "Counting files…";
            DriveList.IsEnabled = false;

            var progress = new Progress<long>(n => ScanText.Text = $"{n:N0} files scanned");
            IProgress<long> p = progress;
            DiskScan scan = null;
            try { scan = await Task.Run(() => DiskScan.Run(drive.Root, n => p.Report(n), token)); }
            catch (Exception ex) { Log.Write("Disk scan: " + ex); }

            DriveList.IsEnabled = true;
            ScanCard.Visibility = Visibility.Collapsed;
            if (token.IsCancellationRequested || scan == null) return;

            _scan = scan;
            Toolbar.Visibility = Visibility.Visible;
            TabFolders.IsChecked = true;
            Show(scan.Root);
            MainWindow.Instance.ShowToast($"Scanned {scan.FileCount:N0} files · {Format.Bytes(scan.TotalBytes)}.");
        }

        void Show(string folder)
        {
            if (_scan == null) return;
            _current = folder;
            if (TabLargest.IsChecked == true)
            {
                PathText.Text = "Largest files on " + _scan.Root.TrimEnd('\\');
                PathSize.Text = "";
                BackButton.IsEnabled = false;
                ItemList.ItemsSource = _scan.LargestFiles();
                return;
            }
            PathText.Text = folder;
            PathSize.Text = Format.Bytes(_scan.SizeOf(folder));
            BackButton.IsEnabled = !string.Equals(folder.TrimEnd('\\'), _scan.Root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            ItemList.ItemsSource = _scan.ItemsIn(folder);
        }

        void Item_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is DiskItem item && item.IsFolder) Show(item.FullPath);
        }

        void Back_Click(object sender, RoutedEventArgs e)
        {
            if (_current == null || _scan == null) return;
            string parent = Path.GetDirectoryName(_current.TrimEnd('\\')) ?? _scan.Root;
            if (parent.Length < _scan.Root.TrimEnd('\\').Length) parent = _scan.Root;
            Show(parent);
        }

        void Tab_Checked(object sender, RoutedEventArgs e)
        {
            if (_scan == null) return;
            Show(TabLargest.IsChecked == true ? _current : (_current ?? _scan.Root));
        }

        void Reveal_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (sender is FrameworkElement fe && fe.DataContext is DiskItem item)
                Shell.Open("explorer.exe", item.IsFolder ? $"\"{item.FullPath}\"" : $"/select,\"{item.FullPath}\"");
        }

        void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();
    }
}
