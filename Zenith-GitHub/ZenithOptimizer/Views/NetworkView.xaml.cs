using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Zenith.Core;

namespace Zenith.Views
{
    public partial class NetworkView : UserControl
    {
        public NetworkView()
        {
            InitializeComponent();
            ProviderList.ItemsSource = DnsSwitcher.Providers;
            Loaded += async (s, e) => await RefreshCurrent();
        }

        async Task RefreshCurrent()
        {
            Spinner.Visibility = Visibility.Visible;
            string servers = null, adapters = null;
            await Task.Run(() =>
            {
                DnsSwitcher.DetectCurrent();
                servers = DnsSwitcher.CurrentServers();
                adapters = string.Join(", ", DnsSwitcher.ActiveAdapters().Select(a => a.Name));
            });
            var current = DnsSwitcher.Providers.FirstOrDefault(p => p.IsCurrent);
            CurrentTitle.Text = $"Using {current?.Name ?? "Automatic"}";
            CurrentText.Text = string.IsNullOrEmpty(adapters) ? "No active connection found." : $"{servers}   ·   on {adapters}";
            Spinner.Visibility = Visibility.Collapsed;
        }

        async void Test_Click(object sender, RoutedEventArgs e)
        {
            TestButton.IsEnabled = false;
            foreach (var p in DnsSwitcher.Providers) p.Latency = "…";
            await Task.Run(() => DnsSwitcher.MeasureAll());
            TestButton.IsEnabled = true;
            var fastest = DnsSwitcher.Providers.FirstOrDefault(p => p.IsFastest);
            if (fastest != null) MainWindow.Instance.ShowToast($"{fastest.Name} is the fastest from your connection ({fastest.Latency}).");
        }

        async void Use_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not DnsProvider p) return;
            ProviderList.IsEnabled = false;
            string err = await Task.Run(() => DnsSwitcher.Apply(p));
            ProviderList.IsEnabled = true;
            MainWindow.Instance.ShowToast(err ?? $"Now using {p.Name} DNS.", err != null);
            await RefreshCurrent();
        }
    }
}
