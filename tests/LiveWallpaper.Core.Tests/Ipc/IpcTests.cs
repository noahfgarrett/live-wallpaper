using System.Diagnostics;
using System.Text;
using LiveWallpaper.Core.Ipc;
using LiveWallpaper.Core.Models;

namespace LiveWallpaper.Core.Tests.Ipc;

public sealed class IpcTests
{
    private static string UniquePipe() => $"lw-test-{Guid.NewGuid():N}";

    public static TheoryData<EngineRequest> AllRequests() =>
    [
        new ApplyWallpaper(Guid.NewGuid(), "all", WallpaperScaling.Fit),
        new UpdateSnow(Guid.NewGuid(), new SnowSettings(DensityPct: 75, MotionBlur: true)),
        new SetPaused(true),
        new SetPreferences(FrameRateMode.Adaptive, WallpaperScaling.Center, false),
        new GetStatus(),
        new Reload(),
        new Shutdown(),
    ];

    [Theory]
    [MemberData(nameof(AllRequests))]
    public async Task Requests_RoundTripWithTypeDiscriminator(EngineRequest request)
    {
        using var stream = new MemoryStream();
        await PipeFraming.WriteAsync(stream, request, IpcJsonContext.Default.EngineRequest, TestContext.Current.CancellationToken);
        stream.Position = 0;

        var read = await PipeFraming.ReadAsync(stream, IpcJsonContext.Default.EngineRequest, TestContext.Current.CancellationToken);

        Assert.Equal(request, read);
    }

    [Fact]
    public async Task Framing_ReadsSequentialMessages()
    {
        using var stream = new MemoryStream();
        await PipeFraming.WriteAsync<EngineRequest>(stream, new SetPaused(true), IpcJsonContext.Default.EngineRequest, TestContext.Current.CancellationToken);
        await PipeFraming.WriteAsync<EngineRequest>(stream, new Reload(), IpcJsonContext.Default.EngineRequest, TestContext.Current.CancellationToken);
        stream.Position = 0;

        Assert.Equal(new SetPaused(true), await PipeFraming.ReadAsync(stream, IpcJsonContext.Default.EngineRequest, TestContext.Current.CancellationToken));
        Assert.Equal(new Reload(), await PipeFraming.ReadAsync(stream, IpcJsonContext.Default.EngineRequest, TestContext.Current.CancellationToken));
        Assert.Null(await PipeFraming.ReadAsync(stream, IpcJsonContext.Default.EngineRequest, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Framing_RejectsOversizedMessage()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', PipeFraming.MaxMessageBytes + 10)));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PipeFraming.ReadAsync(stream, IpcJsonContext.Default.EngineRequest, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Server_HandlesRequestAndReplies()
    {
        var name = UniquePipe();
        await using var server = new EnginePipeServer(name, EnginePipeServer.DefaultPipeFactory(name),
            r => Task.FromResult<EngineResponse>(r is GetStatus ? new Failed("status seen") : new Ok()));
        server.Start();

        var response = await new EngineClient(name).SendAsync(new GetStatus(), TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        Assert.Equal(new Failed("status seen"), response);
    }

    [Fact]
    public async Task Server_ServesSequentialClients()
    {
        var name = UniquePipe();
        var count = 0;
        await using var server = new EnginePipeServer(name, EnginePipeServer.DefaultPipeFactory(name),
            _ => Task.FromResult<EngineResponse>(new Failed($"{Interlocked.Increment(ref count)}")));
        server.Start();
        var client = new EngineClient(name);

        for (var i = 1; i <= 3; i++)
        {
            Assert.Equal(new Failed($"{i}"), await client.SendAsync(new Reload(), TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public async Task EngineClient_ConnectTimesOut_ReturnsNotRunning()
    {
        var watch = Stopwatch.StartNew();

        var response = await new EngineClient(UniquePipe()).SendAsync(new GetStatus(), TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        Assert.Null(response);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task UpdateCoalescer_SendsLatestOnly()
    {
        var sent = new List<int>();
        await using (var coalescer = new UpdateCoalescer<int>(async v =>
        {
            lock (sent)
            {
                sent.Add(v);
            }
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }, TimeSpan.Zero))
        {
            for (var i = 1; i <= 100; i++)
            {
                coalescer.Post(i);
            }
        }

        Assert.Equal(100, sent[^1]);
        Assert.True(sent.Count <= 5, $"sent {sent.Count}");
    }

    [Fact]
    public async Task UpdateCoalescer_RespectsMinimumInterval()
    {
        var times = new List<long>();
        var watch = Stopwatch.StartNew();
        await using (var coalescer = new UpdateCoalescer<int>(_ =>
        {
            times.Add(watch.ElapsedMilliseconds);
            return Task.CompletedTask;
        }, TimeSpan.FromMilliseconds(100)))
        {
            coalescer.Post(1);
            await Task.Delay(10, TestContext.Current.CancellationToken);
            coalescer.Post(2);
        }

        Assert.Equal(2, times.Count);
        Assert.True(times[1] - times[0] >= 90, $"gap {times[1] - times[0]} ms");
    }

    [Fact]
    public void PipeName_IsStableAndSafe()
    {
        Assert.Equal(EnginePipe.Name, EnginePipe.Name);
        Assert.Matches("^LiveWallpaper\\.Engine\\.[A-Za-z0-9_-]+$", EnginePipe.Name);
    }
}
