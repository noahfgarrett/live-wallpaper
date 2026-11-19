namespace LiveWallpaper.Core.Snow;

public sealed class FallClock
{
    private double _time;

    public double Step(double dtSec, double speedPct) => StepScaled(dtSec, SnowModel.SpeedMultiplier(speedPct));

    public double StepScaled(double dtSec, double multiplier)
    {
        _time += Math.Max(0, dtSec) * multiplier;
        return _time;
    }
}
