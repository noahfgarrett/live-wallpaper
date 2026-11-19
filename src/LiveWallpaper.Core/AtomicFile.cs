namespace LiveWallpaper.Core;

public static class AtomicFile
{
    private const int Attempts = 12;
    private const int BaseDelayMs = 15;

    public static void Replace(string tempPath, string destinationPath)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(tempPath, destinationPath, overwrite: true);
                return;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && attempt < Attempts)
            {
                Thread.Sleep(BaseDelayMs * attempt);
            }
        }
    }
}
