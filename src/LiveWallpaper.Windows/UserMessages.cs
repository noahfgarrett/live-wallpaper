using LiveWallpaper.Windows.Interop;

namespace LiveWallpaper.Windows;

public static class UserMessages
{
    public const string Caption = "Live Wallpaper";

    public static void ShowWarning(string text) => User32.MessageBox(0, text, Caption, User32.MB_OK | User32.MB_ICONWARNING);
}
