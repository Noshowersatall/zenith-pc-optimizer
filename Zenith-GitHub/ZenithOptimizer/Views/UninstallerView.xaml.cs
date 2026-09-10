using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Zenith.Core;

namespace Zenith.Views
{
    public partial class UninstallerView : UserControl
    {
        List<InstalledApp> _all = new List<InstalledApp>();

        public UninstallerView()
        {
            InitializeComponent();
            Loaded += async (s, e) => await LoadAsync();
        }

        async Task LoadAsync()
        {
            Spinner.Visibility = Visibility.Visible;
            _all = await Task.Run(() => Uninstaller.Load());
            Spinner.Visibility = Visibility.Collapsed;
            long total = _all.Sum(a => a.SizeBytes);
            Summary.Text = $"{_all.Count} desktop programs installed · {Format.Bytes(total)} reported. Each program's own uninstaller runs, so it removes everything properly.";
            ApplyView();
        }

        void ApplyView()
        {
            if (AppList == null) return;
            string q = Search.Text?.Trim() ?? "";
            IEnumerable<InstalledApp> items = _all;
            if (q.Length > 0)
                items = items.Where(a => a.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                      || (a.Publisher ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));
            if (SortSize.IsChecked == true) items = items.OrderByDescending(a => a.SizeBytes);
            else if (SortDate.IsChecked == true) items = items.OrderByDescending(a => a.InstallDate ?? DateTime.MinValue);
            AppList.ItemsSource = items.ToList();
        }

        void Sort_Checked(object sender, RoutedEventArgs e) => ApplyView();
        void Search_TextChanged(object sender, TextChangedEventArgs e) => ApplyView();

        async void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not InstalledApp app) return;
            var mw = MainWindow.Instance;
            if (!await mw.Confirm($"Uninstall {app.Name}?", "The program's own uninstaller will open — follow its steps to finish.", "Uninstall", "\uE738")) return;
            bool removed = false;
            await mw.RunBusy($"Uninstalling {app.Name}", async report =>
            {
                report("Waiting for the uninstaller to finish…", 0.3);
                await Uninstaller.Uninstall(app);
                removed = !Uninstaller.StillInstalled(app);
                return removed ? $"{app.Name} was removed." : $"{app.Name} still appears to be installed. If its uninstaller is still open, finish it there.";
            });
            await LoadAsync();
        }
    }
}
