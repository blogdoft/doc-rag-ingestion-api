using System.Text.Json;
using DocRag.Ingestion.Domain.MasterData;
using FluentValidation;

namespace DocRag.Ingestion.Domain.Documents;

/// <summary>The editable descriptive fields of a document, validated as one unit.</summary>
/// <param name="Title">Human readable title.</param>
/// <param name="Description">Optional summary.</param>
/// <param name="Type">Category master data record.</param>
/// <param name="Language">Language master data record.</param>
/// <param name="Tags">Distinct labels.</param>
/// <param name="Metadata">Free-form JSON object.</param>
internal sealed record DocumentFields(
    string? Title,
    string? Description,
    DocumentType? Type,
    DocumentLanguage? Language,
    IReadOnlyList<string> Tags,
    JsonElement Metadata);

internal sealed partial class DocumentFieldsValidator : AbstractValidator<DocumentFields>
{
    public const int MaxMetadataBytes = 64 * 1024;
    public const int MaxMetadataDepth = 16;

    public DocumentFieldsValidator()
    {
        RuleFor(x => x.Title).NotEmpty().WithMessage("title is required.")
            .MaximumLength(300).WithMessage("title must have at most 300 characters.");
        RuleFor(x => x.Description).MaximumLength(4000).WithMessage("description must have at most 4000 characters.");
        RuleFor(x => x.Type).NotNull().WithMessage("type is required.");
        RuleFor(x => x.Language).NotNull().WithMessage("language is required.");
        RuleFor(x => x.Tags).Must(tags => tags.Count <= 50).WithMessage("tags must have at most 50 items.")
            .Must(tags => tags.Distinct(StringComparer.Ordinal).Count() == tags.Count).WithMessage("tags must be unique.")
            .Must(tags => tags.All(tag => !string.IsNullOrWhiteSpace(tag) && tag.Length <= 80))
            .WithMessage("each tag must have between 1 and 80 characters.");
        RuleFor(x => x.Metadata).Must(metadata => metadata.ValueKind == JsonValueKind.Object)
            .WithMessage("metadata must be a JSON object.")
            .Must(metadata => metadata.ValueKind != JsonValueKind.Object || metadata.GetRawText().Length <= MaxMetadataBytes)
            .WithMessage("metadata must have at most 64 KiB.")
            .Must(metadata => metadata.ValueKind != JsonValueKind.Object || Depth(metadata, 1) <= MaxMetadataDepth)
            .WithMessage("metadata must have at most 16 nesting levels.");
    }

    private static int Depth(JsonElement element, int level) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().Select(p => Depth(p.Value, level + 1)).DefaultIfEmpty(level).Max(),
        JsonValueKind.Array => element.EnumerateArray().Select(i => Depth(i, level + 1)).DefaultIfEmpty(level).Max(),
        _ => level,
    };
}
