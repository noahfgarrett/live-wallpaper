namespace LiveWallpaper.Windows;

public sealed class SingleInstance : IDisposable
{
    public const string EngineMutexName = @"Local\LiveWallpaper.Engine";

    private readonly Mutex _mutex;

    private SingleInstance(Mutex mutex)
    {
        _mutex = mutex;
    }

    public static SingleInstance? TryAcquire(string name)
    {
        var mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
        if (createdNew)
        {
            return new SingleInstance(mutex);
        }
        mutex.Dispose();
        return null;
    }

    public void Dispose()
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
