using System.Text.Json;
using DocRag.Ingestion.Domain.Common;
using DocRag.Ingestion.Domain.MasterData;

namespace DocRag.Ingestion.Domain.Documents;

/// <summary>A JSON Merge Patch over the mutable fields of a <see cref="Document"/>.</summary>
public sealed record DocumentPatch
{
    /// <summary>Gets the new title; <see langword="null"/> is rejected.</summary>
    public Optional<string?> Title { get; init; }

    /// <summary>Gets the new description; <see langword="null"/> clears it.</summary>
    public Optional<string?> Description { get; init; }

    /// <summary>Gets the new type; <see langword="null"/> is rejected.</summary>
    public Optional<DocumentType?> Type { get; init; }

    /// <summary>Gets the new language; <see langword="null"/> is rejected.</summary>
    public Optional<DocumentLanguage?> Language { get; init; }

    /// <summary>Gets the replacement tags; <see langword="null"/> clears them.</summary>
    public Optional<IReadOnlyList<string>?> Tags { get; init; }

    /// <summary>Gets the metadata patch, merged recursively; <see langword="null"/> clears the metadata.</summary>
    public Optional<JsonElement?> Metadata { get; init; }
}
