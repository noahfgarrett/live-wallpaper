using System.IO.Pipes;
using System.Text.Json;
using LiveWallpaper.Core.Diagnostics;

namespace LiveWallpaper.Core.Ipc;

public sealed class EnginePipeServer(string pipeName, Func<NamedPipeServerStream> createPipe, Func<EngineRequest, Task<EngineResponse>> handle) : IAsyncDisposable
{
    private static readonly TimeSpan s_clientTimeout = TimeSpan.FromSeconds(5);
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;

    public static Func<NamedPipeServerStream> DefaultPipeFactory(string name) =>
        () => new NamedPipeServerStream(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    public void Start() => _loop ??= Task.Run(() => RunAsync(_stop.Token));

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
        _stop.Dispose();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = createPipe();
            }
            catch (IOException ex)
            {
                Log.Error("ipc", $"event=pipe-create-failed name={pipeName}", ex);
                await Task.Delay(1000, ct).ConfigureAwait(false);
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
                    var response = request is null ? new Failed("Empty request.") : await handle(request).ConfigureAwait(false);
                    await PipeFraming.WriteAsync(pipe, response, IpcJsonContext.Default.EngineResponse, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or OperationCanceledException or NotSupportedException)
                {
                    Log.Error("ipc", "event=request-failed", ex);
                }
            }
        }
    }
}
