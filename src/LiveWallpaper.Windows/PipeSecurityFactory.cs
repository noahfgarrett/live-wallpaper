using System.IO.Pipes;

namespace LiveWallpaper.Windows;

public static class PipeSecurityFactory
{
    public static Func<NamedPipeServerStream> CurrentUserOnly(string pipeName) => () =>
        new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
}
