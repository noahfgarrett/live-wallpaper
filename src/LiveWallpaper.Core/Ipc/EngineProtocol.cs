namespace LiveWallpaper.Core.Ipc;

public static class EngineProtocol
{
    public const int Current = 1;

    public static bool NeedsReplacement(EngineStatus status) => status.ProtocolVersion < Current;
}
