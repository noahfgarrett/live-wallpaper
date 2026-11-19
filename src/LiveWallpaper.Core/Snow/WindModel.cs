namespace LiveWallpaper.Core.Snow;

public sealed class WindModel
{
    private const double DefaultBaseWindPxPerSec = 120;
    private const double GustStrength = 0.8;
    private static readonly double[] s_frequenciesHz = [0.071, 0.113, 0.029];
    private static readonly double[] s_weights = [1, 0.6, 0.4];

    private readonly double _baseWindPxPerSecAtFull;
    private readonly double[] _phases = new double[3];
    private double _time;
    private double _offsetPx;

    public WindModel(uint seed)
        : this(seed, DefaultBaseWindPxPerSec)
    {
    }

    public WindModel(uint seed, double basePxPerSecAtFull)
    {
        _baseWindPxPerSecAtFull = basePxPerSecAtFull;
        var state = seed;
        for (var i = 0; i < _phases.Length; i++)
        {
            _phases[i] = Mulberry32(ref state) * Math.PI * 2;
        }
    }

    public (double WindPxPerSec, double OffsetPx) Step(double dtSec, double windPct, double heightPx)
    {
        var dt = Math.Max(0, dtSec);
        _time += dt;
        var baseWind = _baseWindPxPerSecAtFull * (SnowModel.ClampPct(windPct) / 100) * (heightPx / SnowModel.ReferenceHeightPx);
        var g = 0.0;
        for (var i = 0; i < s_frequenciesHz.Length; i++)
        {
            g += s_weights[i] * Math.Sin((2 * Math.PI * s_frequenciesHz[i] * _time) + _phases[i]);
        }
        g /= 2;
        var positive = Math.Max(0, g);
        var wind = baseWind + (positive * positive * GustStrength * baseWind);
        _offsetPx += wind * dt;
        return (wind, _offsetPx);
    }

    private static double Mulberry32(ref uint state)
    {
        unchecked
        {
            state += 0x6D2B79F5;
            var t = state;
            t = (t ^ (t >> 15)) * (t | 1);
            t ^= t + ((t ^ (t >> 7)) * (t | 61));
            return (t ^ (t >> 14)) / 4294967296.0;
        }
    }
}
