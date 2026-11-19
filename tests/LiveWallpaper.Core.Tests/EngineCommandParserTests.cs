using LiveWallpaper.Core.Models;

namespace LiveWallpaper.Core.Tests;

public sealed class EngineCommandParserTests
{
    [Fact]
    public void Parse_NoArguments_Runs() => Assert.Equal(new EngineCommand.Run(), EngineCommandParser.Parse([]));

    [Fact]
    public void Parse_Stop() => Assert.Equal(new EngineCommand.Stop(), EngineCommandParser.Parse(["--stop"]));

    [Fact]
    public void Parse_ListMonitors() => Assert.Equal(new EngineCommand.ListMonitors(), EngineCommandParser.Parse(["--list-monitors"]));

    [Fact]
    public void Parse_DumpDesktop() =>
        Assert.Equal(new EngineCommand.DumpDesktop("d.json"), EngineCommandParser.Parse(["--dump-desktop", "d.json"]));

    [Fact]
    public void Parse_SetWithDefaults_TargetsAllMonitorsWithFill() =>
        Assert.Equal(new EngineCommand.SetWallpaper(@"C:\a.jpg", "all", WallpaperScaling.Fill),
            EngineCommandParser.Parse(["--set", @"C:\a.jpg"]));

    [Fact]
    public void Parse_SetWithMonitorAndScaling() =>
        Assert.Equal(new EngineCommand.SetWallpaper("a.jpg", "ID1", WallpaperScaling.Fit),
            EngineCommandParser.Parse(["--set", "a.jpg", "--monitor", "ID1", "--scaling", "fit"]));

    [Fact]
    public void Parse_ScalingIsCaseInsensitive() =>
        Assert.Equal(new EngineCommand.SetWallpaper("a.jpg", "all", WallpaperScaling.Center),
            EngineCommandParser.Parse(["--set", "a.jpg", "--scaling", "Center"]));

    [Fact]
    public void Parse_SetWithoutPath_IsInvalid() => Assert.IsType<EngineCommand.Invalid>(EngineCommandParser.Parse(["--set"]));

    [Fact]
    public void Parse_UnknownScaling_IsInvalid() =>
        Assert.IsType<EngineCommand.Invalid>(EngineCommandParser.Parse(["--scaling", "zoom", "--set", "a"]));

    [Fact]
    public void Parse_UnknownFlag_IsInvalid() => Assert.IsType<EngineCommand.Invalid>(EngineCommandParser.Parse(["--frobnicate"]));

    [Fact]
    public void Parse_MonitorWithoutSet_IsInvalid() =>
        Assert.IsType<EngineCommand.Invalid>(EngineCommandParser.Parse(["--monitor", "ID1"]));

    [Fact]
    public void Parse_TwoVerbs_IsInvalid() => Assert.IsType<EngineCommand.Invalid>(EngineCommandParser.Parse(["--stop", "--list-monitors"]));
}
