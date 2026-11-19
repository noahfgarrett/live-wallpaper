namespace LiveWallpaper.Core.Snow;

public sealed class FallClock
{
    private double _time;

    public double Step(double dtSec, double speedPct)
    {
        _time += Math.Max(0, dtSec) * SnowModel.SpeedMultiplier(speedPct);
        return _time;
    }
}
