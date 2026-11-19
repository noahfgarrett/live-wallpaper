using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace LiveWallpaper.Windows.Interop;

[GeneratedComInterface]
[Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
internal partial interface IDesktopWallpaper
{
    [PreserveSig] int SetWallpaper(nint monitorId, nint wallpaper);
    [PreserveSig] int GetWallpaper(nint monitorId, out nint wallpaper);
    [PreserveSig] int GetMonitorDevicePathAt(uint monitorIndex, out nint monitorId);
    [PreserveSig] int GetMonitorDevicePathCount(out uint count);
    [PreserveSig] int GetMonitorRECT(nint monitorId, out RECT displayRect);
    [PreserveSig] int SetBackgroundColor(uint color);
    [PreserveSig] int GetBackgroundColor(out uint color);
    [PreserveSig] int SetPosition(int position);
    [PreserveSig] int GetPosition(out int position);
    [PreserveSig] int SetSlideshow(nint items);
    [PreserveSig] int GetSlideshow(out nint items);
    [PreserveSig] int SetSlideshowOptions(int options, uint slideshowTick);
    [PreserveSig] int GetSlideshowOptions(out int options, out uint slideshowTick);
    [PreserveSig] int AdvanceSlideshow(nint monitorId, int direction);
    [PreserveSig] int GetStatus(out uint state);
    [PreserveSig] int Enable([MarshalAs(UnmanagedType.Bool)] bool enable);
}

internal static partial class DesktopWallpaperCom
{
    private const uint ClsctxLocalServer = 0x4;
    private const uint DssSlideshow = 0x2;
    private static readonly Guid s_clsid = new("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD");
    private static readonly Guid s_iid = typeof(IDesktopWallpaper).GUID;

    public static bool? IsSlideshowActive()
    {
        var hr = CoCreateInstance(s_clsid, 0, ClsctxLocalServer, s_iid, out var pointer);
        if (hr < 0 || pointer == 0)
        {
            return null;
        }
        try
        {
            var wallpaper = (IDesktopWallpaper)new StrategyBasedComWrappers().GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.None);
            return wallpaper.GetStatus(out var state) >= 0 ? (state & DssSlideshow) != 0 : null;
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint pointer);
}
