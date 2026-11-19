namespace LiveWallpaper.Core.Diagnostics;

public static class Log
{
    private static RollingLog? s_log;
    private static bool s_isDebug;

    public static void Initialize(RollingLog log, bool isDebug)
    {
        s_log = log;
        s_isDebug = isDebug;
    }

    public static void Info(string prefix, string message) => Write("INFO", prefix, message);

    public static void Debug(string prefix, string message)
    {
        if (s_isDebug)
        {
            Write("DEBUG", prefix, message);
        }
    }

    public static void Error(string prefix, string message, Exception? ex = null) =>
        Write("ERROR", prefix, ex is null ? message : $"{message} exception={ex.GetType().Name} detail=\"{ex.Message}\"");

    private static void Write(string level, string prefix, string message)
    {
        try
        {
            s_log?.Write(level, prefix, message);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
