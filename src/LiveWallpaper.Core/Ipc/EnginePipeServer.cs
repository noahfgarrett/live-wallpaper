using System.IO.Pipes;
using LiveWallpaper.Core.Diagnostics;

namespace LiveWallpaper.Core.Ipc;

public sealed class EnginePipeServer(string pipeName, Func<NamedPipeServerStream> createPipe, Func<EngineRequest, Task<EngineResponse>> handle) : IAsyncDisposable
{
    private const int Listeners = 4;
    private static readonly TimeSpan s_clientTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan s_retryDelay = TimeSpan.FromSeconds(1);

    private readonly CancellationTokenSource _stop = new();
    private Task[] _loops = [];

    public static Func<NamedPipeServerStream> DefaultPipeFactory(string name) =>
        () => new NamedPipeServerStream(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    public void Start()
    {
        if (_loops.Length == 0)
        {
            _loops = Enumerable.Range(0, Listeners).Select(_ => Task.Run(() => ListenAsync(_stop.Token))).ToArray();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        try
        {
            await Task.WhenAll(_loops).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error("ipc", "event=server-stop-failed", ex);
        }
        _stop.Dispose();
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = createPipe();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Error("ipc", $"event=pipe-create-failed name={pipeName}", ex);
                try
                {
                    await Task.Delay(s_retryDelay, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                continue;
            }

            await using (pipe.ConfigureAwait(false))
            {
                try
                {
                    await pipe.WaitForConnectionAsync(ct).ConfigureAwait(false);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(s_clientTimeout);
                    var request = await PipeFraming.ReadAsync(pipe, IpcJsonContext.Default.EngineRequest, timeout.Token).ConfigureAwait(false);
                    var response = request is null
                        ? new Failed("Empty request.")
                        : await handle(request).WaitAsync(timeout.Token).ConfigureAwait(false);
                    await PipeFraming.WriteAsync(pipe, response, IpcJsonContext.Default.EngineResponse, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Log.Error("ipc", "event=request-failed", ex);
                }
            }
        }
    }
}
