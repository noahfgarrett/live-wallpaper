using LiveWallpaper.Core.Diagnostics;

namespace LiveWallpaper.Core.Ipc;

public sealed class UpdateCoalescer<T>(Func<T, Task> send, TimeSpan minInterval) : IAsyncDisposable
{
    private readonly Lock _gate = new();
    private T? _pending;
    private bool _hasPending;
    private Task? _running;
    private DateTime _lastSentUtc = DateTime.MinValue;

    public void Post(T value)
    {
        lock (_gate)
        {
            _pending = value;
            _hasPending = true;
            _running ??= RunAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? running;
        lock (_gate)
        {
            running = _running;
        }
        if (running is not null)
        {
            await running.ConfigureAwait(false);
        }
    }

    private async Task RunAsync()
    {
        while (true)
        {
            var wait = minInterval - (DateTime.UtcNow - _lastSentUtc);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait).ConfigureAwait(false);
            }

            T value;
            lock (_gate)
            {
                if (!_hasPending)
                {
                    _running = null;
                    return;
                }
                value = _pending!;
                _hasPending = false;
            }

            _lastSentUtc = DateTime.UtcNow;
            try
            {
                await send(value).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Log.Error("ipc", "event=coalesced-send-failed", ex);
            }
        }
    }
}
