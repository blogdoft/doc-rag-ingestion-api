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

    /// <summary>Category controlled by the consuming application.</summary>
    /// <example>fiscal-manual</example>
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Type { get; init; } = string.Empty;

    /// <summary>Language as a BCP 47 tag.</summary>
    /// <example>pt-BR</example>
    [Required]
    [RegularExpression("^[a-z]{2,3}(-[A-Za-z0-9]{2,8})*$", ErrorMessage = "language must be a BCP 47 tag, e.g. pt-BR.")]
    public string Language { get; init; } = string.Empty;

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

    /// <summary>New category. Cannot be null.</summary>
    public PatchField<string?> Type { get; init; }

    /// <summary>New BCP 47 language tag. Cannot be null.</summary>
    public PatchField<string?> Language { get; init; }

    /// <summary>Replacement tags; null clears them.</summary>
    public PatchField<List<string>?> Tags { get; init; }

    /// <summary>Metadata patch, merged recursively into the current metadata; null members remove keys.</summary>
    public PatchField<JsonElement?> Metadata { get; init; }

    /// <summary>Maps to the domain patch.</summary>
    /// <returns>The patch.</returns>
    public DocumentPatch ToPatch() => new()
    {
        Title = Title.ToOptional(),
        Description = Description.ToOptional(),
        Type = Type.ToOptional(),
        Language = Language.ToOptional(),
        Tags = Tags.IsSet
            ? Domain.Common.Optional<IReadOnlyList<string>?>.Of(Tags.Value)
            : Domain.Common.Optional<IReadOnlyList<string>?>.Unset,
        Metadata = Metadata.ToOptional(),
    };

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!(Title.IsSet || Description.IsSet || Type.IsSet || Language.IsSet || Tags.IsSet || Metadata.IsSet))
        {
            yield return new ValidationResult("The patch must change at least one property.");
        }

        if (Title is { IsSet: true, Value: null })
        {
            yield return new ValidationResult("title cannot be null.", [nameof(Title)]);
        }

        if (Type is { IsSet: true, Value: null })
        {
            yield return new ValidationResult("type cannot be null.", [nameof(Type)]);
        }

        if (Language is { IsSet: true, Value: null })
        {
            yield return new ValidationResult("language cannot be null.", [nameof(Language)]);
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

    /// <summary>Category.</summary>
    /// <example>fiscal-manual</example>
    public string Type { get; init; } = string.Empty;

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
        Type = document.Type,
        Language = document.Language,
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
