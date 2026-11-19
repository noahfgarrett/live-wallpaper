using System.Globalization;
using System.Text;

namespace LiveWallpaper.Core.Diagnostics;

public sealed class RollingLog
{
    private readonly string _directory;
    private readonly string _baseName;
    private readonly long _maxBytes;
    private readonly int _keepFiles;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();

    public RollingLog(string directory, string baseName, long maxBytes = 1_048_576, int keepFiles = 3, TimeProvider? timeProvider = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytes, 64);
        ArgumentOutOfRangeException.ThrowIfLessThan(keepFiles, 1);
        _directory = directory;
        _baseName = baseName;
        _maxBytes = maxBytes;
        _keepFiles = keepFiles;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void Write(string level, string prefix, string message)
    {
        var stamp = _timeProvider.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        var flat = message.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        var line = $"{stamp} {level} [{prefix}] {flat}{Environment.NewLine}";
        var bytes = Encoding.UTF8.GetByteCount(line);

        lock (_gate)
        {
            Directory.CreateDirectory(_directory);
            var current = FilePath(0);
            if (File.Exists(current) && new FileInfo(current).Length + bytes > _maxBytes)
            {
                Roll();
            }
            File.AppendAllText(current, line, Encoding.UTF8);
        }
    }

    private void Roll()
    {
        var oldest = FilePath(_keepFiles - 1);
        if (File.Exists(oldest))
        {
            File.Delete(oldest);
        }
        for (var i = _keepFiles - 2; i >= 0; i--)
        {
            var from = FilePath(i);
            if (File.Exists(from))
            {
                File.Move(from, FilePath(i + 1), overwrite: true);
            }
        }
    }

    private string FilePath(int index) =>
        Path.Combine(_directory, index == 0 ? $"{_baseName}.log" : $"{_baseName}.{index}.log");
}
