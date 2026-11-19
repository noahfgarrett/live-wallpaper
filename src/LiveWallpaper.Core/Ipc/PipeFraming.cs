using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace LiveWallpaper.Core.Ipc;

public static class PipeFraming
{
    public const int MaxMessageBytes = 1024 * 1024;
    private const byte NewLine = (byte)'\n';

    public static async Task WriteAsync<T>(Stream stream, T message, JsonTypeInfo<T> info, CancellationToken ct)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, info);
        if (bytes.Length > MaxMessageBytes)
        {
            throw new InvalidDataException("Message is too large.");
        }
        var framed = new byte[bytes.Length + 1];
        bytes.CopyTo(framed, 0);
        framed[^1] = NewLine;
        await stream.WriteAsync(framed, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async Task<T?> ReadAsync<T>(Stream stream, JsonTypeInfo<T> info, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var one = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(one, ct).ConfigureAwait(false);
            if (read == 0)
            {
                if (buffer.Length == 0)
                {
                    return default;
                }
                break;
            }
            if (one[0] == NewLine)
            {
                break;
            }
            if (buffer.Length >= MaxMessageBytes)
            {
                throw new InvalidDataException("Message is too large.");
            }
            buffer.WriteByte(one[0]);
        }
        return JsonSerializer.Deserialize(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), info);
    }
}
