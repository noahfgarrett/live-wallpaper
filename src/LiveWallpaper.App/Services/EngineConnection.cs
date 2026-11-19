using System.Diagnostics;
using LiveWallpaper.Core.Diagnostics;
using LiveWallpaper.Core.Ipc;

namespace LiveWallpaper.App.Services;

public sealed class EngineConnection(EngineClient client, string engineExePath)
{
    private static readonly TimeSpan s_pingTimeout = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan s_requestTimeout = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan s_startBudget = TimeSpan.FromSeconds(3);
    private const int StartPollMs = 200;

    public string EngineExePath => engineExePath;

    public async Task<bool> EnsureRunningAsync(CancellationToken ct)
    {
        if (await PingAsync(ct).ConfigureAwait(false))
        {
            return true;
        }
        var watch = Stopwatch.StartNew();
        if (!IsEngineProcessRunning())
        {
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
        }
        while (watch.Elapsed < s_startBudget)
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

    private static bool IsEngineProcessRunning()
    {
        var processes = Process.GetProcessesByName("LiveWallpaper.Engine");
        foreach (var process in processes)
        {
            process.Dispose();
        }
        return processes.Length > 0;
    }

    private async Task<bool> PingAsync(CancellationToken ct) =>
        await client.SendAsync(new GetStatus(), s_pingTimeout, ct).ConfigureAwait(false) is StatusReport;
}
