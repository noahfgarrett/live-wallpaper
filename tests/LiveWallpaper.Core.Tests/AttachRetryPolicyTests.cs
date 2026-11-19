using LiveWallpaper.Core.Desktop;

namespace LiveWallpaper.Core.Tests;

public sealed class AttachRetryPolicyTests
{
    [Fact]
    public void NothingToShow_NeverRebuildsWithoutALostWindow()
    {
        var policy = new AttachRetryPolicy();
        policy.RecordBuild(BuildOutcome.NothingToShow);

        Assert.All(Enumerable.Range(0, 100), _ => Assert.False(policy.ShouldRebuildOnCheck(windowLost: false)));
    }

    [Fact]
    public void Shown_RebuildsWhenAWindowIsLost()
    {
        var policy = new AttachRetryPolicy();
        policy.RecordBuild(BuildOutcome.Shown);

        Assert.False(policy.ShouldRebuildOnCheck(windowLost: false));
        Assert.True(policy.ShouldRebuildOnCheck(windowLost: true));
    }

    [Fact]
    public void AttachFailed_RetriesWithGrowingGaps()
    {
        var policy = new AttachRetryPolicy();
        var gaps = new List<int>();
        for (var failure = 0; failure < 7; failure++)
        {
            policy.RecordBuild(BuildOutcome.AttachFailed);
            var checks = 1;
            while (!policy.ShouldRebuildOnCheck(windowLost: false))
            {
                checks++;
            }
            gaps.Add(checks);
        }

        Assert.Equal([1, 2, 4, 8, 12, 12, 12], gaps);
    }

    [Fact]
    public void Shown_ResetsTheBackoff()
    {
        var policy = new AttachRetryPolicy();
        policy.RecordBuild(BuildOutcome.AttachFailed);
        policy.RecordBuild(BuildOutcome.AttachFailed);
        policy.RecordBuild(BuildOutcome.Shown);
        policy.RecordBuild(BuildOutcome.AttachFailed);

        Assert.True(policy.ShouldRebuildOnCheck(windowLost: false));
    }
}
