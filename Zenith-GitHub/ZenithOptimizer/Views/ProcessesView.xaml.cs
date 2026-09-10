using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Zenith.Core;

namespace Zenith.Views
{
    public partial class ProcessesView : UserControl
    {
        readonly ProcessMonitor _monitor = new ProcessMonitor();
        readonly ObservableCollection<ProcGroup> _view = new ObservableCollection<ProcGroup>();
        readonly Dictionary<string, ProcGroup> _groups = new Dictionary<string, ProcGroup>(StringComparer.OrdinalIgnoreCase);
        readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        bool _sampling;

        public ProcessesView()
        {
            InitializeComponent();
            ProcList.ItemsSource = _view;
            _timer.Tick += async (s, e) => await RefreshAsync();
            Loaded += async (s, e) => { await RefreshAsync(); _timer.Start(); };
            Unloaded += (s, e) => _timer.Stop();
        }

        async Task RefreshAsync()
        {
            if (_sampling) return;
            _sampling = true;
            try
            {
                var data = await Task.Run(() => _monitor.Sample());

                foreach (var kv in data)
                {
                    if (!_groups.TryGetValue(kv.Key, out var g))
                        _groups[kv.Key] = g = new ProcGroup(kv.Key) { ExePath = kv.Value.Path, Description = kv.Value.Desc };
                    g.Cpu = kv.Value.Cpu;
                    g.Memory = kv.Value.Mem;
                    g.Pids.Clear();
                    g.Pids.AddRange(kv.Value.Pids);
                    g.Count = kv.Value.Pids.Count;
                }
                foreach (var gone in _groups.Keys.Where(k => !data.ContainsKey(k)).ToList()) _groups.Remove(gone);

                var mem = SystemInfo.Memory();
                CpuTotal.Text = $"{Math.Min(100, data.Values.Sum(v => v.Cpu)):0}%";
                MemTotal.Text = $"{Format.Bytes((long)mem.Used)}  ({mem.Percent:0}%)";
                ProcCount.Text = data.Values.Sum(v => v.Pids.Count).ToString();

                ApplyView();
            }
            catch (Exception ex) { Log.Write("Processes refresh: " + ex.Message); }
            finally { _sampling = false; }
        }

        void ApplyView()
        {
            string filter = Search.Text?.Trim() ?? "";
            IEnumerable<ProcGroup> items = _groups.Values;
            if (filter.Length > 0)
                items = items.Where(g => g.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                                      || (g.Description ?? "").Contains(filter, StringComparison.OrdinalIgnoreCase));
            var ordered = (SortMem.IsChecked == true
                    ? items.OrderByDescending(g => g.Memory)
                    : items.OrderByDescending(g => g.Cpu).ThenByDescending(g => g.Memory))
                .Take(150).ToList();

            // update the collection in place so the list doesn't flicker or lose scroll position
            for (int i = _view.Count - 1; i >= 0; i--)
                if (!ordered.Contains(_view[i])) _view.RemoveAt(i);
            for (int i = 0; i < ordered.Count; i++)
            {
                int cur = _view.IndexOf(ordered[i]);
                if (cur < 0) _view.Insert(i, ordered[i]);
                else if (cur != i) _view.Move(cur, i);
            }
        }

        void Sort_Checked(object sender, RoutedEventArgs e)
        {
            if (ProcList != null) ApplyView();
        }

        void Search_TextChanged(object sender, TextChangedEventArgs e) => ApplyView();

        async void Kill_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not ProcGroup g || g.IsProtected) return;
            string what = g.Count > 1 ? $"all {g.Count} {g.Name} processes" : g.Name;
            if (!await MainWindow.Instance.Confirm($"End {g.DisplayName}?", $"This closes {what} immediately. Unsaved work in it will be lost.", "End task", "\uE711")) return;
            int failed = await Task.Run(() => ProcessMonitor.Kill(g));
            MainWindow.Instance.ShowToast(failed == 0 ? $"Ended {g.DisplayName}." : $"Windows blocked ending {failed} of its processes.", failed > 0);
            await RefreshAsync();
        }
    }
}
