namespace LiveWallpaper.Windows.Tests;

[Collection(MessageWindowCollection.Name)]
public sealed class MessageWindowTests
{
    [Fact]
    public void FindRunning_FindsCreatedWindow()
    {
        WindowsOnly.Require();
        using var window = new MessageWindow(_ => { });

        Assert.Equal(window.Handle, MessageWindow.FindRunning());
    }

    [Fact]
    public void PostClose_DeliversCloseRequestedThroughMessageLoop()
    {
        WindowsOnly.Require();
        var events = new List<MessageWindowEvent>();
        using var window = new MessageWindow(e =>
        {
            events.Add(e);
            MessageWindow.Quit();
        });

        MessageWindow.PostClose(window.Handle);
        window.RunMessageLoop();

        Assert.Equal([MessageWindowEvent.CloseRequested], events);
    }
}
