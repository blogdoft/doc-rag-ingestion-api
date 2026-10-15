using BlogDoFT.Libs.ResultPattern;

namespace DocRag.Ingestion.Application.Common;

/// <summary>Page-size policy shared by every cursor-paginated listing.</summary>
public static class Pagination
{
    /// <summary>Page size used when the client sends none.</summary>
    public const int DefaultPageSize = 20;

    /// <summary>Largest page size a client may request.</summary>
    public const int MaxPageSize = 100;

    /// <summary>Checks a requested page size.</summary>
    /// <param name="pageSize">The requested page size.</param>
    /// <returns>A <c>400</c> failure when out of range; otherwise <see langword="null"/>.</returns>
    public static Failure? Validate(int pageSize) =>
        pageSize is < 1 or > MaxPageSize
            ? new Failure("400-page-size-invalid", $"pageSize must be between 1 and {MaxPageSize}.")
            : null;
}
