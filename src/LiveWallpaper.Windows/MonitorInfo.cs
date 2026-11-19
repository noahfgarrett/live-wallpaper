using System.Drawing;

namespace LiveWallpaper.Windows;

public sealed record MonitorInfo(string Id, string DeviceName, Rectangle Bounds, uint Dpi, bool IsPrimary, double RefreshHz);
