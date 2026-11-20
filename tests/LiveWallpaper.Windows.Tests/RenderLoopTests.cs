using System.Diagnostics;

namespace LiveWallpaper.Windows.Tests;

public sealed class RenderLoopTests
{
    [Fact]
    public void RenderLoop_CallsFrameAtRequestedInterval()
    {
        WindowsOnly.Require();
        using var loop = new RenderLoop();
        var frames = 0;
        var watch = Stopwatch.StartNew();

        loop.Run(() =>
        {
            frames++;
            if (watch.Elapsed >= TimeSpan.FromSeconds(1))
            {
                loop.Stop();
            }
            return TimeSpan.FromMilliseconds(33);
        });

        Assert.InRange(frames, 21, 39);
    }

    [Fact]
    public async Task RenderLoop_IdlesWhenFrameReturnsNullUntilRequested()
    {
        WindowsOnly.Require();
        using var loop = new RenderLoop();
        var frames = 0;
        var helper = Task.Run(async () =>
        {
            await Task.Delay(300);
            loop.RequestFrame();
            await Task.Delay(300);
            loop.Stop();
        }, TestContext.Current.CancellationToken);

        loop.Run(() =>
        {
            frames++;
            return null;
        });
        await helper;

        Assert.Equal(2, frames);
    }

    [Fact]
    public void RenderLoop_StopFromAnotherThreadEndsRun()
    {
        WindowsOnly.Require();
        using var loop = new RenderLoop();
        // A dedicated thread: the thread pool is saturated by the GPU tests on CI.
        var stopper = new Thread(() =>
        {
            Thread.Sleep(200);
            loop.Stop();
        })
        {
            IsBackground = true,
        };
        stopper.Start();

        var watch = Stopwatch.StartNew();
        loop.Run(() => null);
        var elapsed = watch.Elapsed;
        stopper.Join();

        Assert.True(elapsed < TimeSpan.FromSeconds(5));
    }
}
