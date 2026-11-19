namespace LiveWallpaper.Windows.Tests;

/// <summary>
/// Tests that create engine message windows run one at a time: the window class is shared and
/// <see cref="MessageWindow.FindRunning"/> finds whichever window exists.
/// </summary>
public static class MessageWindowCollection
{
    public const string Name = "Engine message windows";
}
