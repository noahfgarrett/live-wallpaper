namespace LiveWallpaper.Windows;

public sealed class SingleAppInstance : IDisposable
{
    public const string AppMutexName = @"Local\LiveWallpaper.App";
    public const string ActivateEventName = @"Local\LiveWallpaper.App.Activate";

    private readonly Mutex _mutex;

    private SingleAppInstance(Mutex mutex)
    {
        _mutex = mutex;
    }

    public static SingleAppInstance? TryAcquire(string name)
    {
        var mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
        if (createdNew)
        {
            return new SingleAppInstance(mutex);
        }
        mutex.Dispose();
        return null;
    }

    public static EventWaitHandle CreateActivationEvent() => new(false, EventResetMode.AutoReset, ActivateEventName);

    public static void SignalExisting()
    {
        if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var handle))
        {
            using (handle)
            {
                handle.Set();
            }
        }
    }

    public void Dispose()
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
