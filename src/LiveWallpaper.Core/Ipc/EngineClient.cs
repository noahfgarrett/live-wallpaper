using System.IO.Pipes;
using System.Text.Json;

namespace LiveWallpaper.Core.Ipc;

public sealed class EngineClient(string pipeName)
{
    private static readonly TimeSpan s_responseTimeout = TimeSpan.FromSeconds(5);

    public async Task<EngineResponse?> SendAsync(EngineRequest request, TimeSpan timeout, CancellationToken ct)
    {
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await client.ConnectAsync(timeout, ct).ConfigureAwait(false);
            using var responseTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            responseTimeout.CancelAfter(s_responseTimeout);
            await PipeFraming.WriteAsync(client, request, IpcJsonContext.Default.EngineRequest, responseTimeout.Token).ConfigureAwait(false);
            return await PipeFraming.ReadAsync(client, IpcJsonContext.Default.EngineResponse, responseTimeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or JsonException or InvalidDataException or UnauthorizedAccessException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return null;
        }
    }
}
