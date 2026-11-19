using LiveWallpaper.Windows.Interop;

namespace LiveWallpaper.Windows;

public enum TrayCommand
{
    None,
    OpenApp,
    PauseResume,
    OpenSettings,
    Exit,
    ApplyRecent,
}

public sealed unsafe class TrayIcon : IDisposable
{
    public const uint CallbackMessage = User32.WM_APP + 1;

    private const uint IconId = 1;
    private const int OpenId = 1;
    private const int PauseId = 2;
    private const int SettingsId = 3;
    private const int ExitId = 4;
    private const int RecentBaseId = 100;

    private readonly nint _window;
    private readonly nint _icon;
    private readonly string _tooltip;
    private bool _added;

    public TrayIcon(nint window, string iconPath, string tooltip)
    {
        _window = window;
        _tooltip = tooltip;
        _icon = File.Exists(iconPath)
            ? User32.LoadImage(0, iconPath, User32.IMAGE_ICON, User32.GetSystemMetrics(User32.SM_CXSMICON), User32.GetSystemMetrics(User32.SM_CYSMICON), User32.LR_LOADFROMFILE)
            : 0;
    }

    public bool Show()
    {
        var data = NewData();
        data.uFlags = Shell32.NIF_MESSAGE | Shell32.NIF_ICON | Shell32.NIF_TIP | Shell32.NIF_SHOWTIP;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = _icon;
        var tip = _tooltip.Length > 127 ? _tooltip[..127] : _tooltip;
        for (var i = 0; i < tip.Length; i++)
        {
            data.szTip[i] = tip[i];
        }
        _added = Shell32.Shell_NotifyIcon(Shell32.NIM_ADD, &data);
        data.uVersion = Shell32.NOTIFYICON_VERSION_4;
        Shell32.Shell_NotifyIcon(Shell32.NIM_SETVERSION, &data);
        return _added;
    }

    public TrayCommand HandleCallback(nint lParam, bool isPaused, IReadOnlyList<(Guid Id, string Name)> recent, out Guid recentId)
    {
        recentId = Guid.Empty;
        var notification = (uint)(lParam & 0xFFFF);
        switch (notification)
        {
            case User32.WM_LBUTTONDBLCLK:
            case User32.NIN_SELECT:
            case User32.NIN_KEYSELECT:
                return TrayCommand.OpenApp;
            case User32.WM_CONTEXTMENU:
                return ShowMenu(isPaused, recent, out recentId);
            default:
                return TrayCommand.None;
        }
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = NewData();
            Shell32.Shell_NotifyIcon(Shell32.NIM_DELETE, &data);
            _added = false;
        }
        if (_icon != 0)
        {
            User32.DestroyIcon(_icon);
        }
    }

    private TrayCommand ShowMenu(bool isPaused, IReadOnlyList<(Guid Id, string Name)> recent, out Guid recentId)
    {
        recentId = Guid.Empty;
        var menu = User32.CreatePopupMenu();
        var submenu = User32.CreatePopupMenu();
        try
        {
            User32.AppendMenu(menu, User32.MF_STRING, OpenId, "Open Live Wallpaper");
            User32.AppendMenu(menu, User32.MF_STRING, PauseId, isPaused ? "Resume animation" : "Pause animation");
            if (recent.Count == 0)
            {
                User32.AppendMenu(submenu, User32.MF_STRING | User32.MF_GRAYED, 0, "Your library is empty");
            }
            for (var i = 0; i < recent.Count; i++)
            {
                User32.AppendMenu(submenu, User32.MF_STRING, (nuint)(RecentBaseId + i), recent[i].Name);
            }
            User32.AppendMenu(menu, User32.MF_POPUP, (nuint)submenu, "Change wallpaper");
            User32.AppendMenu(menu, User32.MF_STRING, SettingsId, "Settings");
            User32.AppendMenu(menu, User32.MF_SEPARATOR, 0, null);
            User32.AppendMenu(menu, User32.MF_STRING, ExitId, "Exit");

            User32.GetCursorPos(out var point);
            User32.SetForegroundWindow(_window);
            var chosen = User32.TrackPopupMenuEx(menu, User32.TPM_RIGHTBUTTON | User32.TPM_RETURNCMD | User32.TPM_NONOTIFY, point.X, point.Y, _window, 0);
            User32.PostMessage(_window, User32.WM_NULL, 0, 0);

            if (chosen >= RecentBaseId && chosen - RecentBaseId < recent.Count)
            {
                recentId = recent[chosen - RecentBaseId].Id;
                return TrayCommand.ApplyRecent;
            }
            return chosen switch
            {
                OpenId => TrayCommand.OpenApp,
                PauseId => TrayCommand.PauseResume,
                SettingsId => TrayCommand.OpenSettings,
                ExitId => TrayCommand.Exit,
                _ => TrayCommand.None,
            };
        }
        finally
        {
            User32.DestroyMenu(menu);
        }
    }

    private NOTIFYICONDATAW NewData() => new()
    {
        cbSize = (uint)sizeof(NOTIFYICONDATAW),
        hWnd = _window,
        uID = IconId,
    };
}
