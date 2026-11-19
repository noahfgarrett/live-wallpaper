using System.Diagnostics;
using LiveWallpaper.Core.Diagnostics;
using LiveWallpaper.Core.Ipc;

namespace LiveWallpaper.App.Services;

public sealed class EngineConnection(EngineClient client, string engineExePath)
{
    private static readonly TimeSpan s_pingTimeout = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan s_requestTimeout = TimeSpan.FromMilliseconds(800);
    private const int StartAttempts = 12;
    private const int StartPollMs = 250;

    public string EngineExePath => engineExePath;

    public async Task<bool> EnsureRunningAsync(CancellationToken ct)
    {
        if (await PingAsync(ct).ConfigureAwait(false))
        {
            return true;
        }
        if (!File.Exists(engineExePath))
        {
            Log.Error("app", "event=engine-missing");
            return false;
        }
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(engineExePath) { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error("app", "event=engine-start-failed", ex);
            return false;
        }
        for (var i = 0; i < StartAttempts; i++)
        {
            await Task.Delay(StartPollMs, ct).ConfigureAwait(false);
            if (await PingAsync(ct).ConfigureAwait(false))
            {
                return true;
            }
        }
        return false;
    }

    public async Task<EngineStatus?> GetStatusAsync(CancellationToken ct) =>
        await client.SendAsync(new GetStatus(), s_requestTimeout, ct).ConfigureAwait(false) is StatusReport report ? report.Status : null;

    public Task<EngineResponse?> SendAsync(EngineRequest request, CancellationToken ct) => client.SendAsync(request, s_requestTimeout, ct);

    private async Task<bool> PingAsync(CancellationToken ct) =>
        await client.SendAsync(new GetStatus(), s_pingTimeout, ct).ConfigureAwait(false) is StatusReport;
}
