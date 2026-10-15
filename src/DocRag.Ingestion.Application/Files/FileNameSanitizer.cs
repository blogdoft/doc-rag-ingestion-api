using System.Globalization;
using System.Text;
using BlogDoFT.Libs.ResultPattern;

namespace DocRag.Ingestion.Application.Files;

/// <summary>Turns a client supplied file name into a safe display name (never used as a storage key).</summary>
public static class FileNameSanitizer
{
    private const int MaxLength = 255;
    private static readonly char[] Forbidden = ['<', '>', ':', '"', '|', '?', '*'];

    /// <summary>Strips directories, control characters and reserved characters.</summary>
    /// <param name="fileName">Raw name from <c>Content-Disposition</c>.</param>
    /// <returns>The sanitized name or a <c>400</c> failure when nothing usable remains.</returns>
    public static Result<string> Sanitize(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return Invalid();
        }

        var name = fileName.Normalize(NormalizationForm.FormC);
        name = name[(name.LastIndexOfAny(['/', '\\']) + 1)..];

        var cleaned = new string(name
            .Where(c => !char.IsControl(c) && Array.IndexOf(Forbidden, c) < 0 && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format)
            .ToArray())
            .Trim(' ', '.');

        if (cleaned.Length == 0)
        {
            return Invalid();
        }

        if (cleaned.Length > MaxLength)
        {
            var extension = Path.GetExtension(cleaned);
            extension = extension.Length > 20 ? string.Empty : extension;
            cleaned = cleaned[..(MaxLength - extension.Length)] + extension;
        }

        return cleaned;
    }

    private static Failure Invalid() =>
        new("400-file-name-invalid", "A valid file name is required in the Content-Disposition header.");
}
