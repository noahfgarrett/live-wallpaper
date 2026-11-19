using System.Drawing;
using LiveWallpaper.Core.Snow;

namespace LiveWallpaper.Core.Tests.Snow;

public sealed class SnowFrameBuilderTests
{
    private static readonly Size Target = new(1920, 1080);

    [Fact]
    public void PhasesStayInUnitRange()
    {
        var c = default(SnowFrameConstants);
        SnowFrameBuilder.Build(123456.789, 98765.4, 40, 77777.7, 50, 50, false, Target, ref c);

        Assert.InRange(c.FallPhase, 0f, 1f);
        Assert.InRange(c.TimePhase, 0f, 1f);
        var spanX = Target.Width + (2 * c.Margin);
        Assert.All([c.WindOffsets.X, c.WindOffsets.Y, c.WindOffsets.Z], o => Assert.InRange(o, 0f, spanX));
    }

    [Fact]
    public void AfterAWeek_StaysBoundedAndSmooth()
    {
        var clock = new FallClock();
        var start = 7 * 24 * 3600.0;
        var c = default(SnowFrameConstants);
        double fallTime = start;
        SnowFrameBuilder.Build(fallTime, 0, 0, start, 100, 50, false, Target, ref c);
        var previous = c.FallPhase;
        for (var i = 0; i < 600; i++)
        {
            fallTime = start + clock.Step(1.0 / 60, 100);
            SnowFrameBuilder.Build(fallTime, 0, 0, start + (i / 60.0), 100, 50, false, Target, ref c);
            Assert.InRange(c.FallPhase, 0f, 1f);
            var delta = (c.FallPhase - previous) * SnowModel.PeriodSeconds;
            if (delta >= 0)
            {
                Assert.True(delta <= (1.0 / 60 * 1.75) + 1e-3, $"frame {i}: delta {delta}");
            }
            previous = c.FallPhase;
        }
    }

    [Fact]
    public void FillsMultipliersAndFlags()
    {
        var c = default(SnowFrameConstants);
        SnowFrameBuilder.Build(0, 0, 25, 0, 100, 0, true, Target, ref c);

        Assert.Equal(1.75f, c.SpeedMul, 5);
        Assert.Equal(0.5f, c.SizeMul, 5);
        Assert.Equal(1f, c.MotionBlur);
        Assert.Equal(25f, c.WindPxPerSec);
        Assert.Equal(1f, c.Scale, 5);
    }

    [Fact]
    public void DoesNotAllocate()
    {
        var c = default(SnowFrameConstants);
        SnowFrameBuilder.Build(1, 1, 1, 1, 50, 50, false, Target, ref c);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            SnowFrameBuilder.Build(i, i * 2, 3, i / 60.0, 50, 50, false, Target, ref c);
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
