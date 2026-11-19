using System.Globalization;
using System.Windows.Controls;
using System.Windows.Threading;
using LiveWallpaper.App.Services;

namespace LiveWallpaper.App.Views;

public partial class PerformanceView : UserControl
{
    private readonly EngineConnection _engine;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _busy;

    public PerformanceView(EngineConnection engine)
    {
        InitializeComponent();
        _engine = engine;
        _timer.Tick += async (_, _) => await RefreshAsync();
        IsVisibleChanged += async (_, _) =>
        {
            if (IsVisible)
            {
                await RefreshAsync();
                _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
        };
    }

    private async Task RefreshAsync()
    {
        if (_busy)
        {
            return;
        }
        _busy = true;
        try
        {
            var status = await _engine.GetStatusAsync(CancellationToken.None);
            EngineWarning.IsOpen = status is null;
            if (status is null)
            {
                Fps.Text = Memory.Text = Cpu.Text = Particles.Text = "–";
                Displays.ItemsSource = null;
                return;
            }
            Fps.Text = status.IsPaused ? "Paused" : $"{status.Fps:0} fps";
            Memory.Text = $"{status.PrivateBytes / (1024.0 * 1024.0):0} MB";
            Cpu.Text = $"{status.CpuPercent.ToString("0.0", CultureInfo.CurrentCulture)}%";
            Particles.Text = status.ActiveParticles.ToString("N0", CultureInfo.CurrentCulture);
            var number = 0;
            Displays.ItemsSource = status.Monitors
                .OrderByDescending(m => m.IsPrimary)
                .Select(m => new
                {
                    Label = $"Display {++number}{(m.IsPrimary ? " (main)" : "")}, {m.Width}×{m.Height}",
                    State = m.IsShown ? "Showing wallpaper" : m.LibraryItemId is null ? "No wallpaper set" : "Not shown",
                })
                .ToList();
        }
        finally
        {
            _busy = false;
        }
    }
}
