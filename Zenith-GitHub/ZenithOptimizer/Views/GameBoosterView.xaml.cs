using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Zenith.Core;

namespace Zenith.Views
{
    public partial class GameBoosterView : UserControl
    {
        readonly ObservableCollection<Game> _games = new ObservableCollection<Game>();
        readonly DispatcherTimer _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };

        public GameBoosterView()
        {
            InitializeComponent();
            GameList.ItemsSource = _games;
            foreach (var g in SettingsStore.Current.Games) _games.Add(new Game(g));
            _statusTimer.Tick += (s, e) => UpdateStatus();
            Loaded += async (s, e) =>
            {
                AutoBoostSwitch.IsChecked = SettingsStore.Current.AutoBoost;
                TraySwitch.IsChecked = SettingsStore.Current.TrayOnClose;
                await Task.Run(() => { foreach (var g in _games) g.Refresh(); });
                AutoStartSwitch.IsChecked = await Task.Run(() => BoostMonitor.IsAutoStartEnabled());
                UpdateStatus();
                _statusTimer.Start();
            };
            Unloaded += (s, e) => _statusTimer.Stop();
        }

        void UpdateStatus()
        {
            var active = BoostMonitor.ActiveGames.ToList();
            foreach (var g in _games) g.IsRunning = active.Contains(g.Name);
            EmptyCard.Visibility = _games.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ListLabel.Text = $"YOUR GAMES  ·  {_games.Count}";

            if (!BoostMonitor.IsRunning)
            {
                StatusTitle.Text = "Live boost is off";
                StatusDot.Fill = (Brush)FindResource("MutedBrush");
                StatusText.Text = _games.Count == 0
                    ? "Add your games below to get started."
                    : "Per-game priority and GPU settings still apply. Turn on Live boost to also free RAM and quiet background apps while you play.";
            }
            else if (active.Count > 0)
            {
                StatusTitle.Text = $"Boosting {string.Join(", ", active)}";
                StatusDot.Fill = (Brush)FindResource("SuccessBrush");
                StatusText.Text = "RAM was cleared and background apps are running at low priority until you quit.";
            }
            else
            {
                StatusTitle.Text = "Ready — watching for games";
                StatusDot.Fill = (Brush)FindResource("CyanBrush");
                StatusText.Text = $"Watching {_games.Count} game{(_games.Count == 1 ? "" : "s")}. Launch one and Zenith boosts it automatically.";
            }
        }

        void Save()
        {
            SettingsStore.Current.Games = _games.Select(g => g.Entry).ToList();
            SettingsStore.Save();
            if (SettingsStore.Current.AutoBoost && _games.Count > 0) BoostMonitor.Start();
            if (_games.Count == 0) BoostMonitor.Stop();
        }

        async Task AddGames(GameEntry[] entries)
        {
            var added = 0;
            foreach (var e in entries)
            {
                if (_games.Any(g => string.Equals(g.ExePath, e.ExePath, StringComparison.OrdinalIgnoreCase))) continue;
                var game = new Game(e);
                await Task.Run(() => GameBooster.ApplyPersistentBoost(e.ExePath));
                game.Refresh();
                _games.Add(game);
                added++;
            }
            Save();
            UpdateStatus();
            MainWindow.Instance.ShowToast(added == 0 ? "Those games are already in your list." : $"Added and boosted {added} game{(added == 1 ? "" : "s")}.");
        }

        async void Detect_Click(object sender, RoutedEventArgs e)
        {
            DetectButton.IsEnabled = false;
            var found = await Task.Run(() => GameBooster.Detect());
            DetectButton.IsEnabled = true;
            var fresh = found.Where(f => !_games.Any(g => string.Equals(g.ExePath, f.ExePath, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (fresh.Length == 0)
            {
                MainWindow.Instance.ShowToast(found.Count == 0
                    ? "No Epic or Steam games found. Use \"Add game\" to pick a game's .exe."
                    : "All detected games are already in your list.");
                return;
            }
            string list = string.Join("\n", fresh.Take(12).Select(f => "•  " + f.Name)) + (fresh.Length > 12 ? $"\n…and {fresh.Length - 12} more" : "");
            if (await MainWindow.Instance.Confirm($"Found {fresh.Length} game{(fresh.Length == 1 ? "" : "s")}", list, "Add & boost", "\uE7FC"))
                await AddGames(fresh);
        }

        async void Add_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Choose a game's .exe",
                Filter = "Games (*.exe)|*.exe",
                CheckFileExists = true
            };
            if (dlg.ShowDialog() != true) return;
            string name;
            try
            {
                var v = FileVersionInfo.GetVersionInfo(dlg.FileName);
                name = !string.IsNullOrWhiteSpace(v.ProductName) ? v.ProductName.Trim() : Path.GetFileNameWithoutExtension(dlg.FileName);
            }
            catch { name = Path.GetFileNameWithoutExtension(dlg.FileName); }
            await AddGames(new[] { new GameEntry { Name = name, ExePath = dlg.FileName } });
        }

        async void Boost_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb || cb.DataContext is not Game game) return;
            bool on = cb.IsChecked == true;
            await Task.Run(() => { if (on) GameBooster.ApplyPersistentBoost(game.ExePath); else GameBooster.RemovePersistentBoost(game.ExePath); });
            game.Refresh();
            MainWindow.Instance.ShowToast($"{game.Name}: {(on ? "high priority + performance GPU on" : "back to Windows defaults")}.");
        }

        async void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not Game game) return;
            await Task.Run(() => GameBooster.RemovePersistentBoost(game.ExePath));
            _games.Remove(game);
            Save();
            UpdateStatus();
            MainWindow.Instance.ShowToast($"Removed {game.Name} and restored its default settings.");
        }

        void AutoBoost_Click(object sender, RoutedEventArgs e)
        {
            bool on = AutoBoostSwitch.IsChecked == true;
            SettingsStore.Current.AutoBoost = on;
            SettingsStore.Save();
            if (on && _games.Count > 0) BoostMonitor.Start(); else BoostMonitor.Stop();
            UpdateStatus();
        }

        void Tray_Click(object sender, RoutedEventArgs e)
        {
            SettingsStore.Current.TrayOnClose = TraySwitch.IsChecked == true;
            SettingsStore.Save();
        }

        async void AutoStart_Click(object sender, RoutedEventArgs e)
        {
            bool on = AutoStartSwitch.IsChecked == true;
            bool ok = await Task.Run(() => BoostMonitor.SetAutoStart(on));
            AutoStartSwitch.IsChecked = await Task.Run(() => BoostMonitor.IsAutoStartEnabled());
            MainWindow.Instance.ShowToast(ok
                ? (on ? "Zenith will start in the tray when you sign in." : "Zenith won't start with Windows.")
                : "Windows didn't accept the change.", !ok);
        }
    }
}
