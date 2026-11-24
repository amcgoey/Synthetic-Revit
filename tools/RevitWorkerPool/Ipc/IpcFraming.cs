using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RevitWorkerPool.Ipc;

/// <summary>
/// Shared newline-delimited UTF-8 framing helpers for control IPC pipes.
/// </summary>
internal static class IpcFraming
{
    public const int DefaultMaxLineBytes = 1024 * 1024;
    public static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static async Task<string?> ReadUtf8LineAsync(
        Stream stream,
        int maxLineBytes = DefaultMaxLineBytes,
        CancellationToken cancellationToken = default)
    {
        var buffer = new byte[1];
        using var lineBytes = new MemoryStream(512);

        while (true)
        {
            int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (bytesRead == 0)
            {
                if (lineBytes.Length == 0)
                {
                    return null;
                }

                break;
            }

            byte b = buffer[0];
            if (b == (byte)'\n')
            {
                break;
            }

            if (lineBytes.Length >= maxLineBytes)
            {
                throw new InvalidDataException($"IPC message exceeded {maxLineBytes} bytes.");
            }

            lineBytes.WriteByte(b);
        }

        byte[] raw = lineBytes.ToArray();
        int count = raw.Length;
        if (count > 0 && raw[count - 1] == (byte)'\r')
        {
            count--;
        }

        return Utf8NoBom.GetString(raw, 0, count);
    }

    public static async Task WriteUtf8LineAsync(
        Stream stream,
        string line,
        CancellationToken cancellationToken = default)
    {
        byte[] payload = Utf8NoBom.GetBytes(line + "\n");
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
