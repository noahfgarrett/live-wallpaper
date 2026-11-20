using System.Drawing;

namespace LiveWallpaper.Core.Geometry;

public sealed record PreviewCamera(Size Render, Size View, double Scale, PointF Offset)
{
    public const double MaxScale = 4.0;
    public const double StepFactor = 1.25;

    private const int MaxRenderWidth = 3840;
    private const int MaxRenderHeight = 2160;
    private const double FitTolerance = 1e-6;
    private const double ActualScale = 1.0;
    private const double ActualTolerance = 1e-9;

    public Size Render { get; init; } = Positive(Render);

    public Size View { get; init; } = Positive(View);

    public double FitScale => ComputeFitScale(Render, View);

    public bool IsFit => Math.Abs(Scale - FitScale) < FitTolerance;

    public int ZoomPercent => (int)Math.Round(Scale * 100, MidpointRounding.AwayFromZero);

    public static PreviewCamera Fit(Size render, Size view)
    {
        var fit = ComputeFitScale(Positive(render), Positive(view));
        return Create(render, view, fit, PointF.Empty);
    }

    public static Size RenderSizeFor(Size monitor)
    {
        var size = Positive(monitor);
        var shrink = Math.Min(1.0, Math.Min((double)MaxRenderWidth / size.Width, (double)MaxRenderHeight / size.Height));
        return new Size(
            Math.Max(1, (int)Math.Round(size.Width * shrink)),
            Math.Max(1, (int)Math.Round(size.Height * shrink)));
    }

    public PreviewCamera ZoomAt(PointF viewPoint, double factor) => WithScaleAround(viewPoint, Scale * factor);

    public PreviewCamera Pan(SizeF viewDelta) =>
        Create(Render, View, Scale, new PointF(Offset.X - (float)(viewDelta.Width / Scale), Offset.Y - (float)(viewDelta.Height / Scale)));

    public PreviewCamera ToFit() => Fit(Render, View);

    public PreviewCamera ToActual() => WithScaleAround(ViewCentre(), ActualScale);

    public PreviewCamera StepIn()
    {
        var next = Scale * StepFactor;
        if (Scale < ActualScale && next > ActualScale)
        {
            next = ActualScale;
        }
        return WithScaleAround(ViewCentre(), next);
    }

    public PreviewCamera StepOut()
    {
        var next = Scale / StepFactor;
        if (Scale > ActualScale && next < ActualScale)
        {
            next = ActualScale;
        }
        return WithScaleAround(ViewCentre(), next);
    }

    public PreviewCamera Resize(Size view)
    {
        var newView = Positive(view);
        if (IsFit)
        {
            return Fit(Render, newView);
        }

        var centre = ToRender(ViewCentre());
        var scale = Math.Clamp(Scale, ComputeFitScale(Render, newView), MaxFor(ComputeFitScale(Render, newView)));
        var offset = new PointF(
            (float)(centre.X - newView.Width / (2 * scale)),
            (float)(centre.Y - newView.Height / (2 * scale)));
        return Create(Render, newView, scale, offset);
    }

    public RectangleF MapRect(RectangleF inRender) => new(
        (float)((inRender.X - Offset.X) * Scale),
        (float)((inRender.Y - Offset.Y) * Scale),
        (float)(inRender.Width * Scale),
        (float)(inRender.Height * Scale));

    public PointF ToRender(PointF view) =>
        new((float)(Offset.X + view.X / Scale), (float)(Offset.Y + view.Y / Scale));

    public PointF ToView(PointF render) =>
        new((float)((render.X - Offset.X) * Scale), (float)((render.Y - Offset.Y) * Scale));

    private PointF ViewCentre() => new(View.Width / 2f, View.Height / 2f);

    private PreviewCamera WithScaleAround(PointF viewPoint, double requested)
    {
        var fit = FitScale;
        var scale = Math.Clamp(requested, fit, MaxFor(fit));
        var anchor = ToRender(viewPoint);
        var offset = new PointF(
            (float)(anchor.X - viewPoint.X / scale),
            (float)(anchor.Y - viewPoint.Y / scale));
        return Create(Render, View, scale, offset);
    }

    private static PreviewCamera Create(Size render, Size view, double scale, PointF offset)
    {
        var camera = new PreviewCamera(render, view, scale, offset);
        var x = ClampAxis(offset.X, camera.Render.Width, camera.View.Width, scale);
        var y = ClampAxis(offset.Y, camera.Render.Height, camera.View.Height, scale);
        if (Math.Abs(scale - ActualScale) < ActualTolerance)
        {
            x = SnapAxis(x, camera.Render.Width, camera.View.Width);
            y = SnapAxis(y, camera.Render.Height, camera.View.Height);
        }
        return camera with { Offset = new PointF(x, y) };
    }

    /// <summary>At 100 % a whole-pixel offset puts every view pixel on a render pixel, so nothing is resampled.</summary>
    private static float SnapAxis(float offset, int render, int view)
    {
        var snapped = MathF.Round(offset);
        return view > render ? snapped : Math.Clamp(snapped, 0, render - view);
    }

    private static float ClampAxis(float offset, int render, int view, double scale)
    {
        var visible = view / scale;
        if (visible > render)
        {
            return (float)((render - visible) / 2);
        }
        return (float)Math.Clamp(offset, 0, render - visible);
    }

    private static double MaxFor(double fit) => Math.Max(MaxScale, fit);

    private static double ComputeFitScale(Size render, Size view) =>
        Math.Min((double)view.Width / render.Width, (double)view.Height / render.Height);

    private static Size Positive(Size size) => new(Math.Max(1, size.Width), Math.Max(1, size.Height));
}
