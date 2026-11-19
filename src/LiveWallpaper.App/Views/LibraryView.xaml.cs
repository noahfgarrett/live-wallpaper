using System.Windows;
using System.Windows.Controls;
using LiveWallpaper.App.Services;
using LiveWallpaper.Core.Ipc;
using LiveWallpaper.Core.Models;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace LiveWallpaper.App.Views;

public partial class LibraryView : UserControl
{
    private const string ImageFilter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp|All files|*.*";

    private readonly MainWindow _window;
    private readonly EngineConnection _engine;
    private readonly LibraryService _library;
    private readonly Dictionary<Guid, LibraryCard> _cards = [];
    private LibraryItem? _pendingRemove;

    public LibraryView(MainWindow window, EngineConnection engine, LibraryService library)
    {
        InitializeComponent();
        _window = window;
        _engine = engine;
        _library = library;
        ApplyPanel.Closed += (_, _) => Overlay.Visibility = Visibility.Collapsed;
        _window.StatusChanged += (_, status) =>
        {
            ShowEngineState(status);
            UpdateBadges();
        };
    }

    public void Refresh()
    {
        var items = _library.Load().OrderByDescending(i => i.ImportedAt).ToList();
        var cards = new List<LibraryCard>(items.Count);
        foreach (var item in items)
        {
            if (!_cards.TryGetValue(item.Id, out var card) || card.Item != item)
            {
                card = new LibraryCard(item, _library.ThumbnailPath(item));
                _cards[item.Id] = card;
            }
            cards.Add(card);
        }
        foreach (var stale in _cards.Keys.Except(items.Select(i => i.Id)).ToList())
        {
            _cards.Remove(stale);
        }
        Cards.ItemsSource = cards;
        EmptyState.Visibility = cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateBadges();
    }

    private void UpdateBadges()
    {
        var shown = _window.LastStatus?.Monitors.Where(m => m.IsShown && m.LibraryItemId is not null).Select(m => m.LibraryItemId!.Value).ToHashSet() ?? [];
        foreach (var card in _cards.Values)
        {
            card.IsOnDesktop = shown.Contains(card.Item.Id);
        }
    }

    private void ShowEngineState(EngineStatus? status)
    {
        EngineWarning.Visibility = status is null ? Visibility.Visible : Visibility.Collapsed;
        ProblemBar.IsOpen = status?.Problem is not null;
        ProblemBar.Message = status?.Problem ?? string.Empty;
    }

    private async void OnImport(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = ImageFilter, Multiselect = true, Title = "Import images" };
        if (dialog.ShowDialog(_window) == true)
        {
            await ImportAsync(dialog.FileNames);
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            await ImportAsync(files.Where(File.Exists));
        }
    }

    private async Task ImportAsync(IEnumerable<string> files)
    {
        var problems = await _library.ImportAsync(files.ToList());
        Refresh();
        if (problems.Count > 0)
        {
            ShowMessage(InfoBarSeverity.Warning, problems.Count == 1 ? "One file was skipped" : $"{problems.Count} files were skipped", string.Join(" ", problems));
        }
    }

    private void OnApplyCard(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is { } card)
        {
            OpenApply(card.Item);
        }
    }

    private void OnEditCard(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is { } card)
        {
            _window.ShowEditor(card.Item);
        }
    }

    private void OnRemoveCard(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is not { } card)
        {
            return;
        }
        _pendingRemove = card.Item;
        RemoveTitle.Text = $"Remove “{card.Item.DisplayName}” from the library?";
        ApplyPanel.Visibility = Visibility.Collapsed;
        RemoveConfirm.Visibility = Visibility.Visible;
        Overlay.Visibility = Visibility.Visible;
    }

    private void OnCancelRemove(object sender, RoutedEventArgs e) => CloseOverlay();

    private async void OnConfirmRemove(object sender, RoutedEventArgs e)
    {
        var item = _pendingRemove;
        CloseOverlay();
        if (item is null)
        {
            return;
        }
        try
        {
            _library.Remove(item.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowMessage(InfoBarSeverity.Error, "Couldn't remove it", $"{item.DisplayName} couldn't be removed from the library.");
            return;
        }
        Refresh();
        await _engine.SendAsync(new Reload(), CancellationToken.None);
        await _window.RefreshEngineStatusAsync();
        ShowMessage(InfoBarSeverity.Informational, "Removed from the library", $"{item.DisplayName} was removed. The original file wasn't touched.");
    }

    private void OpenApply(LibraryItem item)
    {
        RemoveConfirm.Visibility = Visibility.Collapsed;
        Overlay.Visibility = Visibility.Visible;
        ApplyPanel.Open(item, _window.LastStatus?.Settings.DefaultScaling ?? WallpaperScaling.Fill, async (selector, scaling) =>
        {
            var start = await _engine.EnsureRunningAsync(CancellationToken.None);
            if (start != EngineStart.Running)
            {
                return EngineConnection.Describe(start);
            }
            var response = await _engine.SendAsync(new ApplyWallpaper(item.Id, selector, scaling), CancellationToken.None);
            if (response is Failed failed)
            {
                return failed.Message;
            }
            if (response is null)
            {
                return "The wallpaper engine didn't respond.";
            }
            await _window.RefreshEngineStatusAsync();
            ShowMessage(InfoBarSeverity.Success, "Applied", $"{item.DisplayName} is now on your desktop.");
            return null;
        });
    }

    private async void OnStartEngine(object sender, RoutedEventArgs e)
    {
        var start = await _window.StartEngineAsync();
        if (start != EngineStart.Running)
        {
            ShowMessage(InfoBarSeverity.Error, "Couldn't start the engine", EngineConnection.Describe(start));
        }
    }

    private void CloseOverlay()
    {
        _pendingRemove = null;
        RemoveConfirm.Visibility = Visibility.Collapsed;
        Overlay.Visibility = Visibility.Collapsed;
    }

    private void ShowMessage(InfoBarSeverity severity, string title, string message)
    {
        MessageBar.Severity = severity;
        MessageBar.Title = title;
        MessageBar.Message = message;
        MessageBar.IsOpen = true;
    }

    private static LibraryCard? CardOf(object sender) => (sender as FrameworkElement)?.DataContext as LibraryCard;
}
