namespace LiveWallpaper.Core.Water;

public sealed class WaterPhaseClock
{
    private double _phase;

    /// <summary>Advances the phase by dt / cycle, so a stretch change alters the rate and never the position.</summary>
    public double Step(double dtSec, double stretchPct)
    {
        var next = _phase + (Math.Max(0, dtSec) / WaterModel.CycleSeconds(stretchPct));
        var f = next - Math.Floor(next);
        _phase = f >= 1 ? 0 : f;
        return _phase;
    }
}
