using System.Diagnostics;
using LiveWallpaper.Core.Diagnostics;
using LiveWallpaper.Core.Ipc;

namespace LiveWallpaper.App.Services;

public enum EngineStart
{
    Running,
    CouldNotStart,
    OlderEngineInTheWay,
}

public sealed class EngineConnection(EngineClient client, string engineExePath)
{
    private static readonly TimeSpan s_pingTimeout = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan s_requestTimeout = TimeSpan.FromMilliseconds(800);
    private static readonly TimeSpan s_startBudget = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan s_takeoverBudget = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan s_outdatedShutdownBudget = TimeSpan.FromSeconds(3);
    private const int StartPollMs = 200;

    public string EngineExePath => engineExePath;

    /// <summary>What to tell the user when the engine isn't running.</summary>
    public static string Describe(EngineStart start) => start switch
    {
        EngineStart.Running => "The wallpaper engine is running.",
        EngineStart.CouldNotStart => "The wallpaper engine couldn't be started.",
        EngineStart.OlderEngineInTheWay => "An older Live Wallpaper engine is running and couldn't be replaced. Restart your PC, or end LiveWallpaper.Engine in Task Manager.",
        _ => throw new ArgumentOutOfRangeException(nameof(start), start, "Unknown engine start result."),
    };

    /// <summary>
    /// Starts the engine unless a current one already answers. An engine that answers with an older protocol is asked to shut down first.
    /// An engine process that doesn't answer (an older build, or a stuck one) doesn't stop the launch: the new engine replaces it, which takes longer.
    /// </summary>
    public async Task<EngineStart> EnsureRunningAsync(CancellationToken ct)
    {
        var status = await TryGetStatusAsync(ct).ConfigureAwait(false);
        if (status is not null && !EngineProtocol.NeedsReplacement(status))
        {
            return EngineStart.Running;
        }
        if (!File.Exists(engineExePath))
        {
            Log.Error("app", "event=engine-missing");
            return EngineStart.CouldNotStart;
        }
        if (status is not null)
        {
            await ShutDownOutdatedEngineAsync(ct).ConfigureAwait(false);
        }
        var watch = Stopwatch.StartNew();
        var otherEngineRunning = IsEngineProcessRunning();
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(engineExePath) { UseShellExecute = false });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error("app", "event=engine-start-failed", ex);
            return EngineStart.CouldNotStart;
        }
        if (otherEngineRunning)
        {
            Log.Info("app", "event=engine-not-answering action=start-replacement");
        }
        var budget = otherEngineRunning ? s_takeoverBudget : s_startBudget;
        var hasLoggedOutdated = false;
        while (watch.Elapsed < budget)
        {
            await Task.Delay(StartPollMs, ct).ConfigureAwait(false);
            if (await TryGetStatusAsync(ct).ConfigureAwait(false) is not { } started)
            {
                continue;
            }
            if (!EngineProtocol.NeedsReplacement(started))
            {
                return EngineStart.Running;
            }
            if (!hasLoggedOutdated)
            {
                Log.Info("app", "event=engine-outdated action=ignored");
                hasLoggedOutdated = true;
            }
        }
        return otherEngineRunning ? EngineStart.OlderEngineInTheWay : EngineStart.CouldNotStart;
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

    /// <summary>
    /// Asks an outdated engine to exit and waits a little for its pipe to close. A failed send may mean it is already
    /// shutting down, so the wait runs either way; an engine that ignores the request is left to the takeover path.
    /// </summary>
    private async Task ShutDownOutdatedEngineAsync(CancellationToken ct)
    {
        Log.Info("app", "event=engine-outdated action=replace");
        await client.SendAsync(new Shutdown(), s_requestTimeout, ct).ConfigureAwait(false);
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < s_outdatedShutdownBudget)
        {
            await Task.Delay(StartPollMs, ct).ConfigureAwait(false);
            if (await TryGetStatusAsync(ct).ConfigureAwait(false) is null)
            {
                return;
            }
        }
    }

    private async Task<EngineStatus?> TryGetStatusAsync(CancellationToken ct) =>
        await client.SendAsync(new GetStatus(), s_pingTimeout, ct).ConfigureAwait(false) is StatusReport report ? report.Status : null;
}
