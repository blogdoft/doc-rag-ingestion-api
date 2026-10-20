using System.ComponentModel.DataAnnotations;
using DocRag.Ingestion.Application.MasterData;
using DocRag.Ingestion.Domain.MasterData;

namespace DocRag.Ingestion.Api.Contracts;

/// <summary>Request body to register or replace a document language.</summary>
public sealed class DocumentLanguageRequest
{
    /// <summary>Language as a BCP 47 tag, unique ignoring case.</summary>
    /// <example>pt-BR</example>
    [Required]
    [StringLength(35, MinimumLength = 1)]
    [RegularExpression("^[a-z]{2,3}(-[A-Za-z0-9]{2,8})*$", ErrorMessage = "code must be a BCP 47 tag, e.g. pt-BR.")]
    public string Code { get; init; } = string.Empty;
}

/// <summary>A registered document language.</summary>
public sealed class DocumentLanguageResponse
{
    /// <summary>Server generated UUID v7 identifier.</summary>
    /// <example>0199a8bb-7bc4-7000-8000-4d981eb52c93</example>
    public Guid Id { get; init; }

    /// <summary>BCP 47 language tag.</summary>
    /// <example>pt-BR</example>
    public string Code { get; init; } = string.Empty;

    /// <summary>Creation instant (UTC).</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Last update instant (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Maps a domain document language.</summary>
    /// <param name="documentLanguage">The document language.</param>
    /// <returns>The response DTO.</returns>
    public static DocumentLanguageResponse From(DocumentLanguage documentLanguage) => new()
    {
        Id = documentLanguage.PublicId,
        Code = documentLanguage.Code,
        CreatedAt = documentLanguage.CreatedAt,
        UpdatedAt = documentLanguage.UpdatedAt,
    };
}

/// <summary>A page of document languages, newest first.</summary>
public sealed class DocumentLanguagePageResponse
{
    /// <summary>The document languages of this page.</summary>
    public IReadOnlyList<DocumentLanguageResponse> Data { get; init; } = [];

    /// <summary>Opaque cursor for the next page, or null on the last page.</summary>
    public string? NextCursor { get; init; }

    /// <summary>Maps an application page.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The response DTO.</returns>
    public static DocumentLanguagePageResponse From(DocumentLanguagePage page) => new()
    {
        Data = page.Items.Select(DocumentLanguageResponse.From).ToList(),
        NextCursor = page.NextCursor,
    };
}
