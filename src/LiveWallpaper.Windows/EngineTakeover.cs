using System.ComponentModel;
using System.Diagnostics;
using LiveWallpaper.Core.Diagnostics;

namespace LiveWallpaper.Windows;

/// <summary>
/// Lets a newly started engine replace an older or stuck one that still holds the single-instance mutex,
/// for example an engine from an earlier build that has no pipe for the app to talk to.
/// </summary>
public static class EngineTakeover
{
    public const string EngineProcessName = "LiveWallpaper.Engine";

    private static readonly TimeSpan s_pollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan s_afterForceStop = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan s_exitWait = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Acquires the mutex, taking it over from an engine that doesn't answer:
    /// ask it to close, wait up to <paramref name="closeTimeout"/>, then call <paramref name="forceStop"/>.
    /// </summary>
    /// <param name="mutexName">The single-instance mutex.</param>
    /// <param name="currentEngineAnswers">True when the engine holding the mutex answers over the pipe, which means it is current and stays.</param>
    /// <param name="answerGrace">How long to keep asking before deciding it doesn't answer, so an engine that is still starting isn't replaced.</param>
    /// <param name="closeTimeout">How long to wait for it to close after being asked.</param>
    /// <param name="forceStop">Stops it when it won't close, for example <see cref="StopOtherEngines"/>.</param>
    /// <returns>The acquired instance, or null when a current engine is running or the mutex couldn't be freed.</returns>
    public static SingleInstance? TryTakeOver(string mutexName, Func<bool> currentEngineAnswers, TimeSpan answerGrace, TimeSpan closeTimeout, Action forceStop)
    {
        if (SingleInstance.TryAcquire(mutexName) is { } free)
        {
            return free;
        }

        var asking = Stopwatch.StartNew();
        while (true)
        {
            if (currentEngineAnswers())
            {
                Log.Info("engine", "event=already-running");
                return null;
            }
            if (asking.Elapsed >= answerGrace)
            {
                break;
            }
            Thread.Sleep(s_pollInterval);
        }

        var window = MessageWindow.FindRunning();
        Log.Info("engine", $"event=takeover step=ask-to-close window={(window != 0 ? "found" : "none")}");
        if (window != 0)
        {
            MessageWindow.PostClose(window);
        }
        if (WaitForMutex(mutexName, closeTimeout) is { } closed)
        {
            Log.Info("engine", "event=takeover step=closed");
            return closed;
        }

        Log.Info("engine", "event=takeover step=force-stop");
        forceStop();
        if (WaitForMutex(mutexName, s_afterForceStop) is { } stopped)
        {
            Log.Info("engine", "event=takeover step=stopped");
            return stopped;
        }
        Log.Error("engine", "event=takeover-failed");
        return null;
    }

    /// <summary>Kills every other engine process (never this one), then puts the user's normal wallpaper back.</summary>
    public static void StopOtherEngines()
    {
        var self = Environment.ProcessId;
        var stopped = 0;
        foreach (var process in Process.GetProcessesByName(EngineProcessName))
        {
            using (process)
            {
                if (process.Id == self)
                {
                    continue;
                }
                try
                {
                    process.Kill();
                    process.WaitForExit(s_exitWait);
                    stopped++;
                    Log.Info("engine", $"event=killed-stale-engine pid={process.Id}");
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    Log.Error("engine", $"event=kill-stale-engine-failed pid={process.Id}", ex);
                }
            }
        }
        if (stopped > 0)
        {
            DesktopRestorer.RestoreDesktop();
        }
    }

    private static SingleInstance? WaitForMutex(string mutexName, TimeSpan timeout)
    {
        var waiting = Stopwatch.StartNew();
        while (true)
        {
            if (SingleInstance.TryAcquire(mutexName) is { } acquired)
            {
                return acquired;
            }
            if (waiting.Elapsed >= timeout)
            {
                return null;
            }
            Thread.Sleep(s_pollInterval);
        }
    }
}
