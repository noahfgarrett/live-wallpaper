using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LiveWallpaper.App.Services;
using LiveWallpaper.App.Views;
using LiveWallpaper.Core.Ipc;
using LiveWallpaper.Core.Models;

namespace LiveWallpaper.App;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    private static readonly Brush RunningBrush = new SolidColorBrush(Color.FromRgb(0x6C, 0xCB, 0x5F));
    private static readonly Brush StoppedBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x99, 0x8A));

    private readonly EngineConnection _engine;
    private readonly LibraryService _library = new();
    private readonly LibraryView _libraryView;
    private readonly SettingsView _settingsView;
    private readonly PerformanceView _performanceView;
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private EditorView? _editor;
    private int _missedStatus;
    private bool _refreshing;
    private bool _isEngineOutdated;
    private EngineStart _lastStart = EngineStart.Running;

    public MainWindow(EngineConnection engine, AppPage startPage)
    {
        InitializeComponent();
        _engine = engine;
        _libraryView = new LibraryView(this, engine, _library);
        _settingsView = new SettingsView(engine);
        _performanceView = new PerformanceView(engine);
        Navigation.SelectedItem = startPage switch
        {
            AppPage.Settings => NavSettings,
            AppPage.Performance => NavPerformance,
            _ => NavLibrary,
        };
        _statusTimer.Tick += async (_, _) => await RefreshEngineStatusAsync();
        Loaded += async (_, _) =>
        {
            await StartEngineAsync();
            _statusTimer.Start();
        };
    }

    public EngineStatus? LastStatus { get; private set; }

    public event EventHandler<EngineStatus?>? StatusChanged;

    /// <summary>Starts the engine if it isn't answering, and remembers why it couldn't be started for the status line.</summary>
    public async Task<EngineStart> StartEngineAsync()
    {
        _lastStart = await _engine.EnsureRunningAsync(CancellationToken.None);
        await RefreshEngineStatusAsync();
        return _lastStart;
    }

    public async Task RefreshEngineStatusAsync()
    {
        if (_refreshing)
        {
            return;
        }
        _refreshing = true;
        try
        {
            var answer = await _engine.GetStatusAsync(CancellationToken.None);
            _missedStatus = answer is null ? _missedStatus + 1 : 0;
            if (answer is null && _missedStatus < 2 && LastStatus is not null)
            {
                return;
            }
            // An outdated engine counts as not running, so nothing is sent to it and the status line isn't green.
            _isEngineOutdated = answer is not null && EngineProtocol.NeedsReplacement(answer);
            LastStatus = _isEngineOutdated ? null : answer;
        }
        finally
        {
            _refreshing = false;
        }
        if (LastStatus is null)
        {
            EngineDot.Fill = StoppedBrush;
            EngineText.Text = _lastStart == EngineStart.OlderEngineInTheWay || _isEngineOutdated
                ? EngineConnection.Describe(EngineStart.OlderEngineInTheWay)
                : "The wallpaper engine isn't running";
        }
        else
        {
            EngineDot.Fill = RunningBrush;
            var shown = LastStatus.Monitors.Count(m => m.IsShown);
            EngineText.Text = LastStatus.IsPaused
                ? "Animation paused"
                : shown == 0 ? "Engine running" : $"Showing on {(shown == 1 ? "1 display" : $"{shown} displays")}";
        }
        StatusChanged?.Invoke(this, LastStatus);
    }

    public void ShowEditor(LibraryItem item)
    {
        _editor?.Close();
        _editor = new EditorView(this, _engine, _library, item);
        Navigation.SelectedItem = null;
        PageHost.Content = _editor;
    }

    public void ShowLibrary()
    {
        Navigation.SelectedItem = NavLibrary;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        _statusTimer.Stop();
        _editor?.Close();
        if (LastStatus is { Settings.KeepRunningWhenAppCloses: false })
        {
            Task.Run(() => _engine.SendAsync(new Shutdown(), CancellationToken.None)).Wait(TimeSpan.FromSeconds(1));
        }
    }

    private void OnNavigate(object sender, SelectionChangedEventArgs e)
    {
        if (Navigation.SelectedItem is null)
        {
            return;
        }
        _editor?.Close();
        _editor = null;
        PageHost.Content = Navigation.SelectedItem switch
        {
            ListBoxItem item when item == NavSettings => _settingsView,
            ListBoxItem item when item == NavPerformance => _performanceView,
            _ => _libraryView,
        };
        if (PageHost.Content == _libraryView)
        {
            _libraryView.Refresh();
        }
    }
}
