using System.Text;

namespace DocRag.Ingestion.Application.Files;

/// <summary>Verifies that the first bytes of a file are compatible with its declared MIME type.</summary>
public static class FileSignature
{
    /// <summary>Number of leading bytes inspected.</summary>
    public const int HeaderLength = 8192;

    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();
    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04];

    /// <summary>Checks the header against the declared type.</summary>
    /// <param name="contentType">Declared MIME type, lowercase and without parameters.</param>
    /// <param name="header">Leading bytes of the file.</param>
    /// <returns><see langword="true"/> when compatible.</returns>
    public static bool Matches(string contentType, ReadOnlySpan<byte> header) => contentType switch
    {
        "application/pdf" => header[..Math.Min(header.Length, 1024)].IndexOf(PdfMagic) >= 0,
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => header.StartsWith(ZipMagic),
        "text/plain" or "text/markdown" or "text/html" => IsText(header),
        _ => false,
    };

    private static bool IsText(ReadOnlySpan<byte> header)
    {
        if (header.IndexOf((byte)0) >= 0 || header.StartsWith(PdfMagic) || header.StartsWith(ZipMagic))
        {
            return false;
        }

        try
        {
            // A multi-byte character may be cut at the end of the window: drop up to 3 trailing bytes.
            for (var trim = 0; trim <= Math.Min(3, header.Length); trim++)
            {
                if (IsStrictUtf8(header[..^trim]))
                {
                    return true;
                }
            }

            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsStrictUtf8(ReadOnlySpan<byte> bytes)
    {
        var encoding = new UTF8Encoding(false, true);
        try
        {
            _ = encoding.GetCharCount(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
