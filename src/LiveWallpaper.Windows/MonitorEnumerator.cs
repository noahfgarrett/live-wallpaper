using System.Runtime.InteropServices;
using LiveWallpaper.Windows.Interop;

namespace LiveWallpaper.Windows;

public static unsafe class MonitorEnumerator
{
    private const uint DefaultDpi = 96;

    public static IReadOnlyList<MonitorInfo> Enumerate()
    {
        var handles = new List<nint>();
        var gc = GCHandle.Alloc(handles);
        try
        {
            User32.EnumDisplayMonitors(0, 0, &OnMonitor, GCHandle.ToIntPtr(gc));
        }
        finally
        {
            gc.Free();
        }

        var monitors = new List<MonitorInfo>(handles.Count);
        foreach (var handle in handles)
        {
            var info = new MONITORINFOEXW { cbSize = (uint)sizeof(MONITORINFOEXW) };
            if (!User32.GetMonitorInfo(handle, ref info))
            {
                continue;
            }
            var deviceName = new string(info.szDevice);
            var dpi = Shcore.GetDpiForMonitor(handle, Shcore.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 ? dpiX : DefaultDpi;
            monitors.Add(new MonitorInfo(
                StableId(deviceName),
                deviceName,
                info.rcMonitor.ToRectangle(),
                dpi,
                (info.dwFlags & User32.MONITORINFOF_PRIMARY) != 0));
        }
        return monitors;
    }

    private static string StableId(string deviceName)
    {
        var device = new DISPLAY_DEVICEW { cb = (uint)sizeof(DISPLAY_DEVICEW) };
        if (User32.EnumDisplayDevices(deviceName, 0, ref device, User32.EDD_GET_DEVICE_INTERFACE_NAME))
        {
            var id = new string(device.DeviceID);
            if (!string.IsNullOrWhiteSpace(id))
            {
                return id;
            }
        }
        return deviceName;
    }

    [UnmanagedCallersOnly]
    private static int OnMonitor(nint monitor, nint hdc, RECT* rect, nint data)
    {
        if (GCHandle.FromIntPtr(data).Target is List<nint> list)
        {
            list.Add(monitor);
        }
        return 1;
    }
}
