using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Zenith.Core;

namespace Zenith.Views
{
    public partial class StartupView : UserControl
    {
        List<StartupEntry> _entries = new List<StartupEntry>();

        public StartupView()
        {
            InitializeComponent();
            Loaded += async (s, e) => await LoadAsync();
        }

        async Task LoadAsync()
        {
            Spinner.Visibility = Visibility.Visible;
            _entries = await Task.Run(() => StartupManager.Load());
            EntryList.ItemsSource = _entries;
            EmptyText.Visibility = _entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            Spinner.Visibility = Visibility.Collapsed;
            UpdateSummary();
        }

        void UpdateSummary()
        {
            int on = _entries.Count(e => e.IsEnabled);
            SummaryTitle.Text = $"{on} of {_entries.Count} apps launch at startup";
            SummarySub.Text = on <= 4
                ? "Nice and lean — your sign-in should be quick."
                : "Tip: keep only what you need immediately (audio, drivers, security). Chat, launchers and updaters can start on demand.";
        }

        async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

        void Toggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb || cb.DataContext is not StartupEntry entry) return;
            bool want = cb.IsChecked == true;
            try
            {
                StartupManager.SetEnabled(entry, want);
                MainWindow.Instance.ShowToast($"{entry.Name} will {(want ? "now" : "no longer")} start with Windows.");
            }
            catch (Exception ex)
            {
                entry.IsEnabled = !want;
                MainWindow.Instance.ShowToast($"Couldn't change {entry.Name}: {ex.Message}", true);
            }
            UpdateSummary();
        }

        void OpenLocation_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is StartupEntry entry && entry.CanOpenLocation)
                Shell.Open("explorer.exe", $"/select,\"{entry.ExePath}\"");
        }
    }
}
