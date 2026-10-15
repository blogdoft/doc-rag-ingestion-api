using System.Buffers.Text;
using BlogDoFT.Libs.ResultPattern;

namespace DocRag.Ingestion.Application.Common;

/// <summary>Opaque pagination cursor wrapping a public identifier.</summary>
public static class Cursor
{
    /// <summary>Encodes an identifier as URL-safe Base64.</summary>
    /// <param name="id">Last item identifier.</param>
    /// <returns>The opaque cursor.</returns>
    public static string Encode(Guid id) => Base64Url.EncodeToString(id.ToByteArray(bigEndian: true));

    /// <summary>Decodes a cursor.</summary>
    /// <param name="cursor">The opaque cursor, or <see langword="null"/>/empty for the first page.</param>
    /// <returns>The identifier (null for the first page), or a <c>400</c> failure for a malformed cursor.</returns>
    public static Result<Guid?> Decode(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
        {
            return (Guid?)null;
        }

        try
        {
            var bytes = Base64Url.DecodeFromChars(cursor);
            return bytes.Length == 16 ? new Guid(bytes, bigEndian: true) : InvalidCursor();
        }
        catch (FormatException)
        {
            return InvalidCursor();
        }
    }

    private static Failure InvalidCursor() => new("400-cursor-invalid", "The cursor is not valid.");
}
