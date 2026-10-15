using System.Security.Cryptography;

namespace DocRag.Ingestion.Application.Files;

/// <summary>
/// Read-only pass-through stream that counts bytes, computes SHA-256 incrementally and aborts
/// with <see cref="UploadTooLargeException"/> beyond the limit. Memory use is constant.
/// </summary>
public sealed class HashingLimitedStream(Stream inner, long maxBytes) : Stream
{
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private string? _digest;

    /// <summary>Gets the number of bytes read so far.</summary>
    public long BytesRead { get; private set; }

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => BytesRead;
        set => throw new NotSupportedException();
    }

    /// <summary>Finalizes and returns the lowercase hexadecimal SHA-256 of everything read.</summary>
    /// <returns>The digest.</returns>
    public string GetDigest() => _digest ??= Convert.ToHexStringLower(_hash.GetHashAndReset());

    /// <inheritdoc />
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken);
        Account(buffer.Span[..read]);
        return read;
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, count);
        Account(buffer.AsSpan(offset, read));
        return read;
    }

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hash.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Account(ReadOnlySpan<byte> data)
    {
        BytesRead += data.Length;
        if (BytesRead > maxBytes)
        {
            throw new UploadTooLargeException(maxBytes);
        }

        _hash.AppendData(data);
    }
}
