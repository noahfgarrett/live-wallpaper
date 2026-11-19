using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

namespace LiveWallpaper.Windows;

public static class PipeSecurityFactory
{
    public static Func<NamedPipeServerStream> CurrentUserOnly(string pipeName) => () =>
    {
        using var identity = WindowsIdentity.GetCurrent();
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(identity.User!, PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
            0,
            0,
            security);
    };
}
