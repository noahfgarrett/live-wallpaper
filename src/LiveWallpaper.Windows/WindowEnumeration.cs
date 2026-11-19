using System.Runtime.InteropServices;
using LiveWallpaper.Windows.Interop;

namespace LiveWallpaper.Windows;

internal static unsafe class WindowEnumeration
{
    public static List<nint> TopLevel()
    {
        var list = new List<nint>();
        var gc = GCHandle.Alloc(list);
        try
        {
            User32.EnumWindows(&Collect, GCHandle.ToIntPtr(gc));
        }
        finally
        {
            gc.Free();
        }
        return list;
    }

    public static List<nint> DirectChildren(nint parent)
    {
        var list = new List<nint>();
        var gc = GCHandle.Alloc(list);
        try
        {
            User32.EnumChildWindows(parent, &Collect, GCHandle.ToIntPtr(gc));
        }
        finally
        {
            gc.Free();
        }
        return list.Where(child => User32.GetParent(child) == parent).ToList();
    }

    [UnmanagedCallersOnly]
    private static int Collect(nint hwnd, nint data)
    {
        if (GCHandle.FromIntPtr(data).Target is List<nint> list)
        {
            list.Add(hwnd);
        }
        return 1;
    }
}
