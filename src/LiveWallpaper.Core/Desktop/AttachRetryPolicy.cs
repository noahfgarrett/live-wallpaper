namespace LiveWallpaper.Core.Desktop;

public enum BuildOutcome
{
    Shown,
    NothingToShow,
    AttachFailed,
}

public sealed class AttachRetryPolicy
{
    private static readonly int[] s_backoffChecks = [1, 2, 4, 8, 12];

    private BuildOutcome _lastOutcome = BuildOutcome.NothingToShow;
    private int _consecutiveFailures;
    private int _checksSinceBuild;

    public void RecordBuild(BuildOutcome outcome)
    {
        _lastOutcome = outcome;
        _consecutiveFailures = outcome == BuildOutcome.AttachFailed ? _consecutiveFailures + 1 : 0;
        _checksSinceBuild = 0;
    }

    public bool ShouldRebuildOnCheck(bool windowLost)
    {
        _checksSinceBuild++;
        if (windowLost)
        {
            return true;
        }
        if (_lastOutcome != BuildOutcome.AttachFailed)
        {
            return false;
        }
        var wait = s_backoffChecks[Math.Min(_consecutiveFailures, s_backoffChecks.Length) - 1];
        return _checksSinceBuild >= wait;
    }
}
