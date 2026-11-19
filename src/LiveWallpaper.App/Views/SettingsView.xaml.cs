using System.Windows;
using System.Windows.Controls;
using LiveWallpaper.App.Services;
using LiveWallpaper.Core.Ipc;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Windows;

namespace LiveWallpaper.App.Views;

public partial class SettingsView : UserControl
{
    private readonly EngineConnection _engine;
    private readonly StartupRegistration _startup = new();
    private bool _loading;

    public SettingsView(EngineConnection engine)
    {
        InitializeComponent();
        _engine = engine;
        IsVisibleChanged += async (_, _) =>
        {
            if (IsVisible)
            {
                await LoadAsync();
            }
        };
    }

    private async Task LoadAsync()
    {
        _loading = true;
        StartWithWindows.IsChecked = _startup.IsEnabled();
        var status = await _engine.GetStatusAsync(CancellationToken.None);
        EngineWarning.IsOpen = status is null;
        KeepRunning.IsEnabled = FrameRate.IsEnabled = DefaultScaling.IsEnabled = status is not null;
        if (status is not null)
        {
            KeepRunning.IsChecked = status.Settings.KeepRunningWhenAppCloses;
            Select(FrameRate, status.Settings.FrameRate.ToString());
            Select(DefaultScaling, status.Settings.DefaultScaling.ToString());
        }
        ErrorText.Text = string.Empty;
        _loading = false;
    }

    private void OnStartupChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        try
        {
            if (StartWithWindows.IsChecked == true)
            {
                _startup.Enable(_engine.EngineExePath);
            }
            else
            {
                _startup.Disable();
            }
            ErrorText.Text = string.Empty;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            ErrorText.Text = "Windows didn't allow the startup setting to change.";
        }
    }

    private async void OnPreferenceChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }
        var request = new SetPreferences(
            Enum.TryParse<FrameRateMode>(SelectedTag(FrameRate), out var fps) ? fps : FrameRateMode.Fps60,
            Enum.TryParse<WallpaperScaling>(SelectedTag(DefaultScaling), out var scaling) ? scaling : WallpaperScaling.Fill,
            KeepRunning.IsChecked == true);
        var response = await _engine.SendAsync(request, CancellationToken.None);
        ErrorText.Text = response switch
        {
            Ok => string.Empty,
            Failed failed => failed.Message,
            _ => "The wallpaper engine didn't respond, so the change wasn't saved.",
        };
    }

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;

    private static void Select(ComboBox box, string tag)
    {
        foreach (ComboBoxItem item in box.Items)
        {
            if ((string)item.Tag == tag)
            {
                box.SelectedItem = item;
            }
        }
    }
}
