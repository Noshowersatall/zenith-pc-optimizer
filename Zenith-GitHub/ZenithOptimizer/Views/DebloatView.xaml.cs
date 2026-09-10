using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Zenith.Core;

namespace Zenith.Views
{
    public partial class DebloatView : UserControl
    {
        List<BloatApp> _apps = new List<BloatApp>();
        bool _loaded, _busy;

        public DebloatView()
        {
            InitializeComponent();
            Loaded += async (s, e) => { if (!_loaded) await ScanAsync(); };
        }

        async Task ScanAsync()
        {
            _busy = true;
            LoadingCard.Visibility = Visibility.Visible;
            EmptyCard.Visibility = Visibility.Collapsed;
            AppList.ItemsSource = null;
            UpdateButtons();

            _apps = await Task.Run(() => Debloater.Scan());
            _loaded = true;
            _busy = false;

            LoadingCard.Visibility = Visibility.Collapsed;
            EmptyCard.Visibility = _apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            AppList.ItemsSource = _apps;
            UpdateButtons();
        }

        void UpdateButtons()
        {
            int n = _apps.Count(a => a.IsChecked);
            RemoveText.Text = n == 0 ? "Remove selected" : $"Remove {n} app{(n == 1 ? "" : "s")}";
            RemoveButton.IsEnabled = !_busy && n > 0;
            SelectRecButton.IsEnabled = !_busy && _apps.Count > 0;
        }

        void Item_Click(object sender, RoutedEventArgs e) => UpdateButtons();

        void SelectRecommended_Click(object sender, RoutedEventArgs e)
        {
            foreach (var a in _apps) a.IsChecked = a.Recommended;
            UpdateButtons();
        }

        async void Remove_Click(object sender, RoutedEventArgs e)
        {
            var selected = _apps.Where(a => a.IsChecked).ToList();
            if (selected.Count == 0) return;
            var mw = MainWindow.Instance;
            string list = string.Join(", ", selected.Select(a => a.Name));
            if (!await mw.Confirm($"Remove {selected.Count} app{(selected.Count == 1 ? "" : "s")}?",
                    $"{list}\n\nThis can take a minute. Apps can be reinstalled from the Microsoft Store.", "Remove", "\uE74D")) return;

            await mw.RunBusy("Removing apps", async report =>
            {
                int ok = 0;
                var failed = new List<string>();
                for (int i = 0; i < selected.Count; i++)
                {
                    var app = selected[i];
                    report($"Removing {app.Name}…", (double)i / selected.Count);
                    string err = await Task.Run(() => Debloater.Remove(app));
                    if (err == null) ok++; else failed.Add(app.Name);
                }
                return $"Removed {ok} app{(ok == 1 ? "" : "s")}." +
                       (failed.Count > 0 ? $" Windows wouldn't remove: {string.Join(", ", failed)}." : "");
            });
            await ScanAsync();
        }
    }
}
