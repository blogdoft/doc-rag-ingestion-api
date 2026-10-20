using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using DocRag.Ingestion.Application.Documents;
using DocRag.Ingestion.Domain.Documents;

namespace DocRag.Ingestion.Api.Contracts;

/// <summary>Request body to register a document.</summary>
public sealed class CreateDocumentRequest
{
    /// <summary>Human readable name of the work.</summary>
    /// <example>Manual de Orientações do Contribuinte - CT-e</example>
    [Required]
    [StringLength(300, MinimumLength = 1)]
    public string Title { get; init; } = string.Empty;

    /// <summary>Optional summary of the document.</summary>
    /// <example>Manual técnico de orientação do CT-e</example>
    [StringLength(4000)]
    public string? Description { get; init; }

    /// <summary>Identifier of a registered document type (<c>/api/v1/document-types</c>).</summary>
    /// <example>0199a8bb-7bc4-7000-8000-4d981eb52c93</example>
    [Required]
    public Guid? TypeId { get; init; }

    /// <summary>Identifier of a registered document language (<c>/api/v1/document-languages</c>).</summary>
    /// <example>0199a8bb-7bc4-7000-8000-4d981eb52c94</example>
    [Required]
    public Guid? LanguageId { get; init; }

    /// <summary>Distinct labels (up to 50, each up to 80 characters).</summary>
    /// <example>["cte", "sefaz"]</example>
    [MaxLength(50)]
    public List<string> Tags { get; init; } = [];

    /// <summary>Free-form JSON object (up to 64 KiB, 16 nesting levels). Must not contain secrets.</summary>
    /// <example>{"publisher": "ENCAT", "jurisdiction": "BR"}</example>
    public JsonElement? Metadata { get; init; }
}

/// <summary>JSON Merge Patch (RFC 7396) body to change the descriptive fields of a document.</summary>
public sealed class PatchDocumentRequest : IValidatableObject
{
    /// <summary>New title. Cannot be null.</summary>
    public PatchField<string?> Title { get; init; }

    /// <summary>New description; null removes it.</summary>
    public PatchField<string?> Description { get; init; }

    /// <summary>New document type identifier. Cannot be null.</summary>
    public PatchField<Guid?> TypeId { get; init; }

    /// <summary>New document language identifier. Cannot be null.</summary>
    public PatchField<Guid?> LanguageId { get; init; }

    /// <summary>Replacement tags; null clears them.</summary>
    public PatchField<List<string>?> Tags { get; init; }

    /// <summary>Metadata patch, merged recursively into the current metadata; null members remove keys.</summary>
    public PatchField<JsonElement?> Metadata { get; init; }

    /// <summary>Maps to the application command.</summary>
    /// <returns>The patch command.</returns>
    public PatchDocumentCommand ToCommand() => new()
    {
        Title = Title.ToOptional(),
        Description = Description.ToOptional(),
        TypeId = TypeId.ToOptional(),
        LanguageId = LanguageId.ToOptional(),
        Tags = Tags.IsSet
            ? Domain.Common.Optional<IReadOnlyList<string>?>.Of(Tags.Value)
            : Domain.Common.Optional<IReadOnlyList<string>?>.Unset,
        Metadata = Metadata.ToOptional(),
    };

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!(Title.IsSet || Description.IsSet || TypeId.IsSet || LanguageId.IsSet || Tags.IsSet || Metadata.IsSet))
        {
            yield return new ValidationResult("The patch must change at least one property.");
        }

        if (Title is { IsSet: true, Value: null })
        {
            yield return new ValidationResult("title cannot be null.", [nameof(Title)]);
        }

        if (TypeId is { IsSet: true, Value: null })
        {
            yield return new ValidationResult("typeId cannot be null.", [nameof(TypeId)]);
        }

        if (LanguageId is { IsSet: true, Value: null })
        {
            yield return new ValidationResult("languageId cannot be null.", [nameof(LanguageId)]);
        }

        if (Metadata.Value is { ValueKind: not (JsonValueKind.Object or JsonValueKind.Null) })
        {
            yield return new ValidationResult("metadata must be a JSON object or null.", [nameof(Metadata)]);
        }
    }
}

/// <summary>A registered document.</summary>
public sealed class DocumentResponse
{
    /// <summary>Server generated UUID v7 identifier.</summary>
    /// <example>0199a8bb-7bc4-7000-8000-4d981eb52c93</example>
    public Guid Id { get; init; }

    /// <summary>Title.</summary>
    /// <example>Manual de Orientações do Contribuinte - CT-e</example>
    public string Title { get; init; } = string.Empty;

    /// <summary>Description, when present.</summary>
    public string? Description { get; init; }

    /// <summary>Identifier of the document type.</summary>
    /// <example>0199a8bb-7bc4-7000-8000-4d981eb52c93</example>
    public Guid TypeId { get; init; }

    /// <summary>Name of the document type.</summary>
    /// <example>fiscal-manual</example>
    public string Type { get; init; } = string.Empty;

    /// <summary>Identifier of the document language.</summary>
    /// <example>0199a8bb-7bc4-7000-8000-4d981eb52c94</example>
    public Guid LanguageId { get; init; }

    /// <summary>BCP 47 language tag.</summary>
    /// <example>pt-BR</example>
    public string Language { get; init; } = string.Empty;

    /// <summary>Tags.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Free-form metadata object.</summary>
    public JsonElement Metadata { get; init; }

    /// <summary>Creation instant (UTC).</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Last update instant (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>Maps a domain document.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The response DTO.</returns>
    public static DocumentResponse From(Document document) => new()
    {
        Id = document.PublicId,
        Title = document.Title,
        Description = document.Description,
        TypeId = document.Type.PublicId,
        Type = document.Type.Name,
        LanguageId = document.Language.PublicId,
        Language = document.Language.Code,
        Tags = document.Tags,
        Metadata = document.Metadata,
        CreatedAt = document.CreatedAt,
        UpdatedAt = document.UpdatedAt,
    };
}

/// <summary>A page of documents, newest first.</summary>
public sealed class DocumentPageResponse
{
    /// <summary>The documents of this page.</summary>
    public IReadOnlyList<DocumentResponse> Data { get; init; } = [];

    /// <summary>Opaque cursor for the next page, or null on the last page.</summary>
    public string? NextCursor { get; init; }

    /// <summary>Maps an application page.</summary>
    /// <param name="page">The page.</param>
    /// <returns>The response DTO.</returns>
    public static DocumentPageResponse From(DocumentPage page) => new()
    {
        Data = page.Items.Select(DocumentResponse.From).ToList(),
        NextCursor = page.NextCursor,
    };
}
