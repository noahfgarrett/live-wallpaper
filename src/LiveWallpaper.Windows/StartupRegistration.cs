using Microsoft.Win32;

namespace LiveWallpaper.Windows;

public sealed class StartupRegistration(string runKeyPath = StartupRegistration.RunKey, string valueName = "LiveWallpaper")
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(runKeyPath);
        return key?.GetValue(valueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public void Enable(string engineExePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(runKeyPath, writable: true);
        key.SetValue(valueName, $"\"{engineExePath}\"", RegistryValueKind.String);
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
