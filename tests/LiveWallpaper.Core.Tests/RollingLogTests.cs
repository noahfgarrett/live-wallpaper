using System.Text.RegularExpressions;
using LiveWallpaper.Core.Diagnostics;

namespace LiveWallpaper.Core.Tests;

public sealed partial class RollingLogTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 8, 36, 0, TimeSpan.Zero);
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void Write_FormatsTimestampLevelAndPrefix()
    {
        var log = new RollingLog(_dir.Path, "engine", timeProvider: new FixedTimeProvider(Now));

        log.Write("INFO", "engine", "event=start");

        var line = File.ReadAllLines(_dir.File("engine.log")).Single();
        Assert.Matches(LinePattern(), line);
    }

    [Fact]
    public void Write_PastMaxBytes_RollsToNumberedFile()
    {
        var log = new RollingLog(_dir.Path, "engine", maxBytes: 200, timeProvider: new FixedTimeProvider(Now));

        for (var i = 0; i < 10; i++)
        {
            log.Write("INFO", "engine", $"event=line index={i}");
        }

        Assert.True(File.Exists(_dir.File("engine.1.log")));
        Assert.True(new FileInfo(_dir.File("engine.log")).Length <= 200);
    }

    [Fact]
    public void Write_NeverKeepsMoreThanKeepFiles()
    {
        var log = new RollingLog(_dir.Path, "engine", maxBytes: 100, keepFiles: 3, timeProvider: new FixedTimeProvider(Now));

        for (var i = 0; i < 100; i++)
        {
            log.Write("INFO", "engine", $"event=line index={i}");
        }

        Assert.Equal(["engine.1.log", "engine.2.log", "engine.log"],
            Directory.GetFiles(_dir.Path).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void Write_FlattensNewlinesInMessage()
    {
        var log = new RollingLog(_dir.Path, "engine", timeProvider: new FixedTimeProvider(Now));

        log.Write("ERROR", "engine", "first\nsecond");

        Assert.Single(File.ReadAllLines(_dir.File("engine.log")));
    }

    [GeneratedRegex(@"^2026-11-19T08:36:00\.000Z INFO \[engine\] event=start$")]
    private static partial Regex LinePattern();
}
