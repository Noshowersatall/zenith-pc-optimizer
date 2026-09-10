using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Zenith.Core;

namespace Zenith
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += OnUiException;
            TaskScheduler.UnobservedTaskException += (s, a) => { Log.Write("Unobserved: " + a.Exception); a.SetObserved(); };
            AppDomain.CurrentDomain.UnhandledException += (s, a) => Log.Write("Fatal: " + a.ExceptionObject);
            Log.Write("---- Zenith started ----");
            SystemInfo.Load();
            base.OnStartup(e);

            var window = new Zenith.MainWindow();
            MainWindow = window;
            // Started by "Start with Windows": stay in the tray, Game Booster keeps watching
            bool tray = Array.Exists(e.Args, a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
            if (!tray) window.Show();
        }

        void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Write("UI exception: " + e.Exception);
            e.Handled = true;
            var w = Zenith.MainWindow.Instance;
            if (w != null && w.IsLoaded) w.ShowToast("Something went wrong: " + e.Exception.Message, true);
            else MessageBox.Show(e.Exception.Message, "Zenith Optimizer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
