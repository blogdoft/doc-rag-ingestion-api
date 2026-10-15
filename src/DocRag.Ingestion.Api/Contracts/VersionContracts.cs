using System.ComponentModel.DataAnnotations;
using DocRag.Ingestion.Application.Versions;
using DocRag.Ingestion.Domain.Files;
using DocRag.Ingestion.Domain.Versions;

namespace DocRag.Ingestion.Api.Contracts;

/// <summary>Request body to register a version of a document.</summary>
public sealed class CreateVersionRequest
{
    /// <summary>Editorial version label; unique within the document. Not a file hash.</summary>
    /// <example>4.00</example>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Label { get; init; } = string.Empty;

    /// <summary>Publication date when known; absent dates are never inferred.</summary>
    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>Start of validity, when applicable.</summary>
    public DateTimeOffset? EffectiveFrom { get; init; }

    /// <summary>End of validity, when applicable; must not precede <c>effectiveFrom</c>.</summary>
    public DateTimeOffset? EffectiveTo { get; init; }
}

/// <summary>Public metadata of a stored original file; bucket and object key are never exposed.</summary>
public sealed class FileResponse
{
    /// <summary>File identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>Sanitized original file name.</summary>
    /// <example>MOC_CTe_VisaoGeral_v4.00.pdf</example>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Media type validated by the server.</summary>
    /// <example>application/pdf</example>
    public string ContentType { get; init; } = string.Empty;

    /// <summary>Exact number of bytes received.</summary>
    /// <example>1048576</example>
    public long SizeBytes { get; init; }

    /// <summary>Lowercase hexadecimal SHA-256 computed while reading the stream.</summary>
    public string Sha256 { get; init; } = string.Empty;

    /// <summary>Always <c>uploaded</c>.</summary>
    /// <example>uploaded</example>
    public string Status { get; init; } = "uploaded";

    /// <summary>When the file became available (UTC).</summary>
    public DateTimeOffset UploadedAt { get; init; }

    /// <summary>Maps a domain file.</summary>
    /// <param name="file">The file.</param>
    /// <returns>The response DTO.</returns>
    public static FileResponse From(DocumentFile file) => new()
    {
        Id = file.PublicId,
        FileName = file.OriginalName,
        ContentType = file.ContentType,
        SizeBytes = file.SizeBytes,
        Sha256 = file.Sha256,
        UploadedAt = file.UploadedAt,
    };
}

/// <summary>A document version and the reception state of its original file.</summary>
public sealed class VersionResponse
{
    /// <summary>Version identifier.</summary>
    public Guid Id { get; init; }

    /// <summary>Parent document identifier.</summary>
    public Guid DocumentId { get; init; }

    /// <summary>Editorial label.</summary>
    /// <example>4.00</example>
    public string Label { get; init; } = string.Empty;

    /// <summary>Publication date, or null when unknown.</summary>
    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>Start of validity, or null.</summary>
    public DateTimeOffset? EffectiveFrom { get; init; }

    /// <summary>End of validity, or null.</summary>
    public DateTimeOffset? EffectiveTo { get; init; }

    /// <summary>Reception status: <c>awaiting_upload</c>, <c>uploading</c>, <c>uploaded</c> or <c>upload_failed</c>. Says nothing about downstream processing.</summary>
    /// <example>awaiting_upload</example>
    public string Status { get; init; } = string.Empty;

    /// <summary>The original file, once uploaded; otherwise null.</summary>
    public FileResponse? File { get; init; }

    /// <summary>Creation instant (UTC).</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Last update instant (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Maps a domain version.</summary>
    /// <param name="version">The version.</param>
    /// <returns>The response DTO.</returns>
    public static VersionResponse From(DocumentVersion version) => new()
    {
        Id = version.PublicId,
        DocumentId = version.DocumentPublicId,
        Label = version.Label,
        PublishedAt = version.PublishedAt,
        EffectiveFrom = version.EffectiveFrom,
        EffectiveTo = version.EffectiveTo,
        Status = StatusName(version.Status),
        File = version.File is null ? null : FileResponse.From(version.File),
        CreatedAt = version.CreatedAt,
        UpdatedAt = version.UpdatedAt,
    };

    private static string StatusName(IngestionStatus status) => status switch
    {
        IngestionStatus.AwaitingUpload => "awaiting_upload",
        IngestionStatus.Uploading => "uploading",
        IngestionStatus.Uploaded => "uploaded",
        IngestionStatus.UploadFailed => "upload_failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}

/// <summary>A page of versions, newest first.</summary>
public sealed class VersionPageResponse
{
    /// <summary>The versions of this page.</summary>
    public IReadOnlyList<VersionResponse> Items { get; init; } = [];

    /// <summary>Opaque cursor for the next page, or null on the last page.</summary>
    public string? NextCursor { get; init; }

    /// <summary>Maps an application page.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The response DTO.</returns>
    public static VersionPageResponse From(VersionPage page) => new()
    {
        Items = page.Items.Select(VersionResponse.From).ToList(),
        NextCursor = page.NextCursor,
    };
}
