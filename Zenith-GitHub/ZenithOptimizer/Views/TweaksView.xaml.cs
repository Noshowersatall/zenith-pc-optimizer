using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Zenith.Core;

namespace Zenith.Views
{
    public sealed class TweakGroup
    {
        public TweakGroup(string name, List<Tweak> items) { Name = name; Items = items; }
        public string Name { get; }
        public List<Tweak> Items { get; }
    }

    public partial class TweaksView : UserControl
    {
        readonly List<Tweak> _tweaks;

        public TweaksView(string page, string title, string subtitle)
        {
            InitializeComponent();
            PageTitle.Text = title;
            PageSubtitle.Text = subtitle;
            _tweaks = AppState.Tweaks.Where(t => t.Page == page).ToList();
            GroupList.ItemsSource = _tweaks
                .GroupBy(t => t.Group)
                .Select(g => new TweakGroup(g.Key.ToUpperInvariant(), g.ToList()))
                .ToList();
            Loaded += async (s, e) => await RefreshAsync();
        }

        async Task RefreshAsync()
        {
            LoadSpinner.Visibility = Visibility.Visible;
            await Task.Run(() => { foreach (var t in _tweaks) t.Refresh(); });
            LoadSpinner.Visibility = Visibility.Collapsed;
            UpdateCount();
        }

        void UpdateCount()
        {
            int on = _tweaks.Count(t => t.IsOn);
            CountText.Text = $"{on} of {_tweaks.Count}";
            AppliedBar.Value = _tweaks.Count == 0 ? 0 : 100.0 * on / _tweaks.Count;
            int pendingRec = _tweaks.Count(t => t.Recommended && !t.IsOn);
            ApplyAllButton.IsEnabled = pendingRec > 0;
        }

        async void Toggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb || cb.DataContext is not Tweak tweak) return;
            bool want = cb.IsChecked == true;
            string err = await tweak.SetAsync(want);
            if (err != null)
                MainWindow.Instance.ShowToast($"{tweak.Title}: {err}", true);
            else
            {
                MainWindow.Instance.ShowToast($"{tweak.Title} {(want ? "applied" : "restored to Windows default")}.");
                if (tweak.NeedsRestart) MainWindow.Instance.ShowRestartBanner();
            }
            UpdateCount();
        }

        async void ApplyRecommended_Click(object sender, RoutedEventArgs e)
        {
            var pending = _tweaks.Where(t => t.Recommended && !t.IsOn).ToList();
            if (pending.Count == 0) return;
            var mw = MainWindow.Instance;
            if (!await mw.Confirm($"Apply {pending.Count} recommended tweak{(pending.Count == 1 ? "" : "s")}?",
                    string.Join("\n", pending.Select(t => "•  " + t.Title)), "Apply")) return;

            bool restart = false;
            await mw.RunBusy("Applying optimizations", async report =>
            {
                int ok = 0, failed = 0;
                for (int i = 0; i < pending.Count; i++)
                {
                    report(pending[i].Title, (double)i / pending.Count);
                    string err = await pending[i].SetAsync(true);
                    if (err == null) { ok++; restart |= pending[i].NeedsRestart; } else failed++;
                }
                return $"Applied {ok} tweak{(ok == 1 ? "" : "s")}." +
                       (failed > 0 ? $" {failed} isn't supported on this PC." : "") +
                       (restart ? " Restart your PC to finish." : "");
            });
            if (restart) mw.ShowRestartBanner();
            UpdateCount();
        }
    }
}
