using System.Diagnostics;
using System.Security.Principal;
using System.Text;

namespace LiveWallpaper.Core.Ipc;

public static class EnginePipe
{
    public static string Name { get; } = $"LiveWallpaper.Engine.{Sanitize(UserKey())}.{CurrentSessionId()}";

    private static string UserKey()
    {
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (identity.User?.Value is { } sid)
            {
                return sid;
            }
        }
        return Environment.UserName;
    }

    private static int CurrentSessionId()
    {
        using var process = Process.GetCurrentProcess();
        return process.SessionId;
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_');
        }
        return builder.Length == 0 ? "user" : builder.ToString();
    }
}
