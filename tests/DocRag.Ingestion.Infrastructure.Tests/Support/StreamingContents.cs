using System.Net;
using System.Security.Cryptography;

namespace DocRag.Ingestion.Infrastructure.Tests.Support;

/// <summary>A body without a known length that sends a head, waits for a gate, then sends the tail.</summary>
public sealed class GatedContent(byte[] head, byte[] tail, Task gate) : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        SerializeToStreamAsync(stream, context, CancellationToken.None);

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(head, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        await gate.WaitAsync(cancellationToken);
        await stream.WriteAsync(tail, cancellationToken);
    }

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}

/// <summary>Generates <c>length</c> bytes of a valid-looking PDF on the fly, hashing them as it goes, so nothing is buffered.</summary>
public sealed class GeneratedPdfContent(long length) : HttpContent
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

    public string Sha256 => Convert.ToHexStringLower(_hash.GetHashAndReset());

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        var buffer = new byte[64 * 1024];
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = (byte)('a' + (i % 26));
        }

        var header = "%PDF-1.4\n"u8;
        header.CopyTo(buffer);

        long remaining = length;
        while (remaining > 0)
        {
            var count = (int)Math.Min(buffer.Length, remaining);
            _hash.AppendData(buffer, 0, count);
            await stream.WriteAsync(buffer.AsMemory(0, count));
            remaining -= count;
            buffer[0] = (byte)'a';
            buffer[1] = (byte)'b';
            buffer[2] = (byte)'c';
            buffer[3] = (byte)'d';
            buffer[4] = (byte)'e';
            buffer[5] = (byte)'f';
            buffer[6] = (byte)'g';
            buffer[7] = (byte)'h';
            buffer[8] = (byte)'i';
        }
    }

    protected override bool TryComputeLength(out long len)
    {
        len = 0;
        return false;
    }
}
