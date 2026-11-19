using System.Text;

namespace LiveWallpaper.Core.Ipc;

public static class EnginePipe
{
    public static string Name { get; } = $"LiveWallpaper.Engine.{Sanitize(Environment.UserName)}";

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
