using System.Windows;
using System.Windows.Controls;
using LiveWallpaper.Core.Models;
using LiveWallpaper.Windows;

namespace LiveWallpaper.App.Views;

public partial class ApplyPanel : UserControl
{
    private Func<string, WallpaperScaling, Task<string?>>? _apply;

    public ApplyPanel()
    {
        InitializeComponent();
    }

    public event EventHandler? Closed;

    public void Open(LibraryItem item, WallpaperScaling defaultScaling, Func<string, WallpaperScaling, Task<string?>> apply)
    {
        _apply = apply;
        ItemName.Text = item.DisplayName;
        ErrorText.Visibility = Visibility.Collapsed;
        ApplyButton.IsEnabled = true;
        MonitorChoices.Children.Clear();
        MonitorChoices.Children.Add(new RadioButton { Content = "All displays", Tag = MonitorAssignments.AllMonitors, IsChecked = true, GroupName = "monitor", Margin = new Thickness(0, 0, 0, 6) });
        var monitors = MonitorEnumerator.Enumerate().OrderByDescending(m => m.IsPrimary).ThenBy(m => m.Bounds.X).ToList();
        for (var i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i];
            var label = $"Display {i + 1}{(m.IsPrimary ? " (main)" : "")}, {m.Bounds.Width}×{m.Bounds.Height}";
            MonitorChoices.Children.Add(new RadioButton { Content = label, Tag = m.Id, GroupName = "monitor", Margin = new Thickness(0, 0, 0, 6) });
        }
        foreach (ComboBoxItem option in ScalingChoice.Items)
        {
            if ((string)option.Tag == defaultScaling.ToString())
            {
                ScalingChoice.SelectedItem = option;
            }
        }
        Visibility = Visibility.Visible;
    }

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        if (_apply is null)
        {
            return;
        }
        var selector = MonitorChoices.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? MonitorAssignments.AllMonitors;
        var scaling = Enum.TryParse<WallpaperScaling>((ScalingChoice.SelectedItem as ComboBoxItem)?.Tag as string, out var parsed) ? parsed : WallpaperScaling.Fill;
        ApplyButton.IsEnabled = false;
        var error = await _apply(selector, scaling);
        ApplyButton.IsEnabled = true;
        if (error is null)
        {
            Close();
            return;
        }
        ErrorText.Text = error;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void Close()
    {
        Visibility = Visibility.Collapsed;
        Closed?.Invoke(this, EventArgs.Empty);
    }
}
