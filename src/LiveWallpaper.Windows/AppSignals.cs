namespace LiveWallpaper.Windows;

public static class AppSignals
{
    public const string AppExitEventName = @"Local\LiveWallpaper.App.Exit";

    public static void RequestAppExit()
    {
        if (EventWaitHandle.TryOpenExisting(AppExitEventName, out var handle))
        {
            using (handle)
            {
                handle.Set();
            }
        }
    }
}
