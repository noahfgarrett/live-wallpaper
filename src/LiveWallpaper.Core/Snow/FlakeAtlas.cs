namespace LiveWallpaper.Core.Snow;

public static class FlakeAtlas
{
    public const int CellPx = 64;
    public const int Cells = 4;

    private static readonly (double At, double Alpha)[] s_softStops = [(0, 1), (0.35, 0.8), (1, 0)];
    private static readonly (double At, double Alpha)[] s_firmStops = [(0, 1), (0.5, 0.92), (0.78, 0.25), (1, 0)];
    private static readonly (double X, double Y, double R)[] s_blobs = [(0, 0, 11), (-9, -5, 7), (8, -7, 6), (6, 9, 7), (-7, 8, 5)];

    public static byte[] GenerateBgra()
    {
        const int width = CellPx * Cells;
        var pixels = new byte[width * CellPx * 4];
        var c = CellPx / 2.0;
        var radius = c - 2;
        for (var cell = 0; cell < Cells; cell++)
        {
            for (var y = 0; y < CellPx; y++)
            {
                for (var x = 0; x < CellPx; x++)
                {
                    var dx = x + 0.5 - c;
                    var dy = y + 0.5 - c;
                    var alpha = cell switch
                    {
                        0 => Radial(Math.Sqrt((dx * dx) + (dy * dy)) / radius, s_softStops),
                        1 => Radial(Math.Sqrt((dx * dx) + (dy * dy)) / radius, s_firmStops),
                        2 => Star(dx, dy),
                        _ => Cluster(dx, dy),
                    };
                    var value = (byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255);
                    var i = ((y * width) + (cell * CellPx) + x) * 4;
                    pixels[i] = value;
                    pixels[i + 1] = value;
                    pixels[i + 2] = value;
                    pixels[i + 3] = value;
                }
            }
        }
        return pixels;
    }

    private static double Radial(double d, (double At, double Alpha)[] stops)
    {
        if (d >= 1)
        {
            return 0;
        }
        for (var i = 1; i < stops.Length; i++)
        {
            if (d <= stops[i].At)
            {
                var t = (d - stops[i - 1].At) / (stops[i].At - stops[i - 1].At);
                return stops[i - 1].Alpha + ((stops[i].Alpha - stops[i - 1].Alpha) * t);
            }
        }
        return 0;
    }

    private static double Star(double x, double y)
    {
        const double halfWidth = 2.25;
        const double soft = 1.6;
        var nearest = double.MaxValue;
        for (var arm = 0; arm < 6; arm++)
        {
            var a = arm / 6.0 * Math.PI * 2;
            nearest = Math.Min(nearest, SegmentDistance(x, y, 0, 0, Math.Cos(a) * 22, Math.Sin(a) * 22));
            var bx = Math.Cos(a) * 12;
            var by = Math.Sin(a) * 12;
            nearest = Math.Min(nearest, SegmentDistance(x, y, bx, by, Math.Cos(a + 0.5) * 17, Math.Sin(a + 0.5) * 17));
            nearest = Math.Min(nearest, SegmentDistance(x, y, bx, by, Math.Cos(a - 0.5) * 17, Math.Sin(a - 0.5) * 17));
        }
        return 1 - SmoothStep(halfWidth - soft, halfWidth + soft, nearest);
    }

    private static double Cluster(double x, double y)
    {
        const double soft = 2.5;
        var alpha = 0.0;
        foreach (var (bx, by, r) in s_blobs)
        {
            var d = Math.Sqrt(((x - bx) * (x - bx)) + ((y - by) * (y - by)));
            alpha = Math.Max(alpha, 1 - SmoothStep(r - soft, r + soft, d));
        }
        return alpha;
    }

    private static double SegmentDistance(double px, double py, double ax, double ay, double bx, double by)
    {
        var vx = bx - ax;
        var vy = by - ay;
        var len2 = (vx * vx) + (vy * vy);
        var t = len2 == 0 ? 0 : Math.Clamp((((px - ax) * vx) + ((py - ay) * vy)) / len2, 0, 1);
        var dx = px - (ax + (t * vx));
        var dy = py - (ay + (t * vy));
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static double SmoothStep(double edge0, double edge1, double x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - (2 * t));
    }
}
