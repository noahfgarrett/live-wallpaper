using System.Windows;
using System.Windows.Threading;
using LiveWallpaper.App.Services;
using LiveWallpaper.Core;
using LiveWallpaper.Core.Diagnostics;
using LiveWallpaper.Core.Ipc;
using LiveWallpaper.Windows;

namespace LiveWallpaper.App;

public partial class App : Application
{
    private SingleAppInstance? _instance;
    private EventWaitHandle? _exitSignal;
    private EventWaitHandle? _activateSignal;
    private RegisteredWaitHandle? _exitWait;
    private RegisteredWaitHandle? _activateWait;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Log.Initialize(new RollingLog(AppPaths.LogDirectory, "app"), Environment.GetEnvironmentVariable("LIVEWALLPAPER_DEBUG") == "1");

        _instance = SingleAppInstance.TryAcquire(SingleAppInstance.AppMutexName);
        if (_instance is null)
        {
            SingleAppInstance.SignalExisting();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandledException;
        _exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, AppSignals.AppExitEventName);
        _exitWait = ThreadPool.RegisterWaitForSingleObject(_exitSignal, (_, _) => Dispatcher.BeginInvoke(Shutdown), null, Timeout.Infinite, executeOnlyOnce: true);
        _activateSignal = SingleAppInstance.CreateActivationEvent();
        _activateWait = ThreadPool.RegisterWaitForSingleObject(_activateSignal, (_, _) => Dispatcher.BeginInvoke(BringToFront), null, Timeout.Infinite, executeOnlyOnce: false);

        var startPage = e.Args.Length >= 2 && e.Args[0] == "--page" && e.Args[1] == "settings" ? AppPage.Settings : AppPage.Library;
        var engine = new EngineConnection(new EngineClient(EnginePipe.Name), Path.Combine(AppContext.BaseDirectory, "LiveWallpaper.Engine.exe"));
        var window = new MainWindow(engine, startPage);
        MainWindow = window;
        window.Show();
        Log.Info("app", "event=start");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exitWait?.Unregister(null);
        _activateWait?.Unregister(null);
        _exitSignal?.Dispose();
        _activateSignal?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }

    private void BringToFront()
    {
        if (MainWindow is not { } window)
        {
            return;
        }
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }
        window.Show();
        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("app", "event=unhandled", e.Exception);
        MessageBox.Show("Something went wrong. The details are in the Live Wallpaper log.", "Live Wallpaper", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
