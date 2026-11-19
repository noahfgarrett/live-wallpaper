using LiveWallpaper.Windows.Interop;

namespace LiveWallpaper.Windows;

public static class ConsoleOutput
{
    public static bool AttachToParent()
    {
        var attached = Kernel32.AttachConsole(Kernel32.ATTACH_PARENT_PROCESS);
        if (attached)
        {
            Console.WriteLine();
        }
        return attached;
    }
}
