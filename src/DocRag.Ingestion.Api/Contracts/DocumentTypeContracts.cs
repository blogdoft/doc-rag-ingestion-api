using System.ComponentModel.DataAnnotations;
using DocRag.Ingestion.Application.MasterData;
using DocRag.Ingestion.Domain.MasterData;

namespace DocRag.Ingestion.Api.Contracts;

/// <summary>Request body to register or replace a document type.</summary>
public sealed class DocumentTypeRequest
{
    /// <summary>Category name, unique ignoring case.</summary>
    /// <example>fiscal-manual</example>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;
}

/// <summary>A registered document type.</summary>
public sealed class DocumentTypeResponse
{
    /// <summary>Server generated UUID v7 identifier.</summary>
    /// <example>0199a8bb-7bc4-7000-8000-4d981eb52c93</example>
    public Guid Id { get; init; }

    /// <summary>Category name.</summary>
    /// <example>fiscal-manual</example>
    public string Name { get; init; } = string.Empty;

    /// <summary>Creation instant (UTC).</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Last update instant (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Maps a domain document type.</summary>
    /// <param name="documentType">The document type.</param>
    /// <returns>The response DTO.</returns>
    public static DocumentTypeResponse From(DocumentType documentType) => new()
    {
        Id = documentType.PublicId,
        Name = documentType.Name,
        CreatedAt = documentType.CreatedAt,
        UpdatedAt = documentType.UpdatedAt,
    };
}

/// <summary>A page of document types, newest first.</summary>
public sealed class DocumentTypePageResponse
{
    /// <summary>The document types of this page.</summary>
    public IReadOnlyList<DocumentTypeResponse> Data { get; init; } = [];

    /// <summary>Opaque cursor for the next page, or null on the last page.</summary>
    public string? NextCursor { get; init; }

    /// <summary>Maps an application page.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The response DTO.</returns>
    public static DocumentTypePageResponse From(DocumentTypePage page) => new()
    {
        Data = page.Items.Select(DocumentTypeResponse.From).ToList(),
        NextCursor = page.NextCursor,
    };
}
