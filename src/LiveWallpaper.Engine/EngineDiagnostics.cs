using System.Diagnostics;

namespace LiveWallpaper.Engine;

public sealed class EngineDiagnostics
{
    private const double IdleAfterSeconds = 2;

    private int _frames;
    private double _windowStart;
    private double _lastFrame = double.NegativeInfinity;
    private double _fps;
    private TimeSpan _lastCpu;
    private long _lastCpuTimestamp;

    public void RecordFrame(double nowSeconds)
    {
        _frames++;
        _lastFrame = nowSeconds;
        if (nowSeconds - _windowStart >= 1)
        {
            _fps = _frames / (nowSeconds - _windowStart);
            _frames = 0;
            _windowStart = nowSeconds;
        }
    }

    public double Fps(double nowSeconds) => nowSeconds - _lastFrame > IdleAfterSeconds ? 0 : Math.Round(_fps, 1);

    public (long PrivateBytes, double CpuPercent) SampleProcess()
    {
        using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        var now = Stopwatch.GetTimestamp();
        var percent = 0.0;
        if (_lastCpuTimestamp != 0)
        {
            var elapsed = Stopwatch.GetElapsedTime(_lastCpuTimestamp, now).TotalSeconds;
            if (elapsed > 0)
            {
                percent = (cpu - _lastCpu).TotalSeconds / elapsed / Environment.ProcessorCount * 100;
            }
        }
        _lastCpu = cpu;
        _lastCpuTimestamp = now;
        return (process.PrivateMemorySize64, Math.Round(percent, 2));
    }
}
