using System.Text.Json;
using LiveWallpaper.Core.Desktop;
using LiveWallpaper.Core.Settings;

namespace LiveWallpaper.Core.Tests;

public sealed class DesktopLayoutClassifierTests
{
    private static DesktopSnapshot Fixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        return JsonSerializer.Deserialize(File.ReadAllText(path), CoreJsonContext.Default.DesktopSnapshot)
            ?? throw new InvalidOperationException($"Fixture {name} was empty.");
    }

    [Fact]
    public void Classify_LegacyLayout_ReturnsWorkerWBelowDefViewHost()
    {
        var plan = DesktopLayoutClassifier.Classify(Fixture("desktop-legacy.json"));

        Assert.Equal(new DesktopAttachPlan.LegacyWorkerW(2003), plan);
    }

    [Fact]
    public void Classify_24H2Layout_ReturnsProgmanChildWithLargestWorkerW()
    {
        var plan = DesktopLayoutClassifier.Classify(Fixture("desktop-24h2.json"));

        Assert.Equal(new DesktopAttachPlan.ProgmanChild(3001, 3002, 3004), plan);
    }

    [Fact]
    public void Classify_UnsplitDesktop_ReturnsUnsupportedNotSplit()
    {
        var plan = DesktopLayoutClassifier.Classify(Fixture("desktop-legacy-unsplit.json"));

        var unsupported = Assert.IsType<DesktopAttachPlan.Unsupported>(plan);
        Assert.Equal(DesktopAttachPlan.NotSplitReason, unsupported.Reason);
    }

    [Fact]
    public void Classify_NoDefView_ReturnsUnsupported()
    {
        Assert.IsType<DesktopAttachPlan.Unsupported>(DesktopLayoutClassifier.Classify(Fixture("desktop-no-defview.json")));
    }

    [Fact]
    public void Classify_LegacyWithoutSpawnedWorkerW_ReturnsUnsupported()
    {
        Assert.IsType<DesktopAttachPlan.Unsupported>(DesktopLayoutClassifier.Classify(Fixture("desktop-legacy-without-workerw.json")));
    }

    [Fact]
    public void Classify_NoProgman_ReturnsUnsupported()
    {
        Assert.IsType<DesktopAttachPlan.Unsupported>(DesktopLayoutClassifier.Classify(new DesktopSnapshot("10.0.26100", [])));
    }

    [Fact]
    public void Snapshot_RoundTripsThroughJson()
    {
        var original = Fixture("desktop-24h2.json");

        var json = JsonSerializer.Serialize(original, CoreJsonContext.Default.DesktopSnapshot);
        var copy = JsonSerializer.Deserialize(json, CoreJsonContext.Default.DesktopSnapshot);

        Assert.NotNull(copy);
        Assert.Equal(original.OsVersion, copy.OsVersion);
        Assert.Equal(original.Windows, copy.Windows);
    }
}
