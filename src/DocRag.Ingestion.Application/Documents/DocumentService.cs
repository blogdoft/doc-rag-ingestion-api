using System.Text.Json;
using BlogDoFT.Libs.ResultPattern;
using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Application.Common;
using DocRag.Ingestion.Application.Idempotency;
using DocRag.Ingestion.Application.MasterData;
using DocRag.Ingestion.Domain.Common;
using DocRag.Ingestion.Domain.Documents;
using DocRag.Ingestion.Domain.MasterData;

namespace DocRag.Ingestion.Application.Documents;

/// <summary>Input of <see cref="DocumentService.CreateAsync"/>.</summary>
/// <param name="IdempotencyKey">Client key, if any.</param>
/// <param name="Title">Title.</param>
/// <param name="Description">Description.</param>
/// <param name="TypeId">Public identifier of the document type.</param>
/// <param name="LanguageId">Public identifier of the document language.</param>
/// <param name="Tags">Tags.</param>
/// <param name="Metadata">Free-form JSON object.</param>
public sealed record CreateDocumentCommand(
    string? IdempotencyKey,
    string? Title,
    string? Description,
    Guid? TypeId,
    Guid? LanguageId,
    IReadOnlyList<string>? Tags,
    JsonElement? Metadata);

/// <summary>Optional exact-match filters of a document listing; a <see langword="null"/> member is not applied.</summary>
/// <param name="TypeId">Public identifier of the document type.</param>
/// <param name="LanguageId">Public identifier of the document language.</param>
/// <param name="Tag">A tag the document must carry.</param>
/// <param name="Title">
/// Case-insensitive title search. <c>*</c> matches any run of characters; without any <c>*</c> the value
/// must equal the whole title.
/// </param>
public sealed record DocumentFilter(Guid? TypeId = null, Guid? LanguageId = null, string? Tag = null, string? Title = null);

/// <summary>A JSON Merge Patch over a document, where type and language are referenced by their public identifiers.</summary>
public sealed record PatchDocumentCommand
{
    /// <summary>Gets the new title.</summary>
    public Optional<string?> Title { get; init; }

    /// <summary>Gets the new description; <see langword="null"/> clears it.</summary>
    public Optional<string?> Description { get; init; }

    /// <summary>Gets the new document type identifier.</summary>
    public Optional<Guid?> TypeId { get; init; }

    /// <summary>Gets the new document language identifier.</summary>
    public Optional<Guid?> LanguageId { get; init; }

    /// <summary>Gets the replacement tags; <see langword="null"/> clears them.</summary>
    public Optional<IReadOnlyList<string>?> Tags { get; init; }

    /// <summary>Gets the metadata patch.</summary>
    public Optional<JsonElement?> Metadata { get; init; }
}

/// <summary>A page of documents.</summary>
/// <param name="Items">The documents, newest first.</param>
/// <param name="NextCursor">Opaque cursor of the next page, or <see langword="null"/> on the last page.</param>
public sealed record DocumentPage(IReadOnlyList<Document> Items, string? NextCursor);

/// <summary>Use cases of the document aggregate.</summary>
public sealed class DocumentService(
    IDocumentRepository documents,
    IDocumentTypeRepository documentTypes,
    IDocumentLanguageRepository documentLanguages,
    IUnitOfWork unitOfWork,
    IdempotencyGate idempotency,
    IIdempotencyStore idempotencyStore,
    IClock clock)
{
    private const string CreateOperation = "create-document";
    private const int MaxTitleFilterLength = 300;

    /// <summary>Registers a document, replaying the original result for a repeated idempotency key.</summary>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The document, or a failure.</returns>
    public async Task<Result<Document>> CreateAsync(CreateDocumentCommand command, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var type = await ResolveTypeAsync(command.TypeId, cancellationToken);
        if (type.IsFailure)
        {
            return type.Failure;
        }

        var language = await ResolveLanguageAsync(command.LanguageId, cancellationToken);
        if (language.IsFailure)
        {
            return language.Failure;
        }

        var built = DocumentBuilder.New()
            .WithPublicId(Guid.CreateVersion7(now))
            .WithTitle(command.Title)
            .WithDescription(command.Description)
            .OfType(type.Value)
            .InLanguage(language.Value)
            .WithTags(command.Tags)
            .WithMetadata(command.Metadata)
            .Build(now);
        if (built.IsFailure)
        {
            return built.Failure;
        }

        var document = built.Value;
        var fingerprint = Fingerprint.Of(
            CreateOperation, document.Title, document.Description, document.Type.PublicId, document.Language.PublicId, document.Tags, document.Metadata);
        var request = idempotency.Describe(CreateOperation, command.IdempotencyKey, fingerprint);

        await using (unitOfWork)
        {
            await unitOfWork.BeginAsync(cancellationToken);
            var decision = await idempotency.EnterAsync(request, cancellationToken);
            if (decision.Failure is not null)
            {
                return decision.Failure;
            }

            if (decision.IsReplay)
            {
                var original = await documents.GetAsync(decision.ReplayResourceId!.Value, cancellationToken);
                return original is null ? new Failure("404-document-not-found", "The original document no longer exists.") : original;
            }

            await documents.InsertAsync(document, cancellationToken);
            if (request is not null)
            {
                await idempotencyStore.CompleteAsync(request, fingerprint, 201, document.PublicId, cancellationToken);
            }

            await unitOfWork.CommitAsync(cancellationToken);
        }

        return document;
    }

    /// <summary>Finds a document.</summary>
    /// <param name="documentId">Public identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The document or a <c>404</c> failure.</returns>
    public async Task<Result<Document>> GetAsync(Guid documentId, CancellationToken cancellationToken)
    {
        var document = await documents.GetAsync(documentId, cancellationToken);
        return document is null ? NotFound() : document;
    }

    /// <summary>Lists documents newest first.</summary>
    /// <param name="filter">Optional filters.</param>
    /// <param name="pageSize">Requested page size (1 to 100).</param>
    /// <param name="cursor">Opaque cursor of the previous page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page, or a <c>400</c> failure.</returns>
    public async Task<Result<DocumentPage>> ListAsync(
        DocumentFilter filter,
        int pageSize,
        string? cursor,
        CancellationToken cancellationToken)
    {
        if (Pagination.Validate(pageSize) is { } invalidPageSize)
        {
            return invalidPageSize;
        }

        var normalized = Normalize(filter);
        if (normalized.Title?.Length > MaxTitleFilterLength)
        {
            return new Failure("400-title-filter-too-long", $"title must have at most {MaxTitleFilterLength} characters.");
        }

        var after = Cursor.Decode(cursor);
        if (after.IsFailure)
        {
            return after.Failure;
        }

        var rows = await documents.ListAsync(normalized, pageSize + 1, after.Value, cancellationToken);
        var items = rows.Take(pageSize).ToList();
        return new DocumentPage(items, rows.Count > pageSize ? Cursor.Encode(items[^1].PublicId) : null);
    }

    /// <summary>Applies a JSON Merge Patch guarded by optimistic concurrency.</summary>
    /// <param name="documentId">Public identifier.</param>
    /// <param name="expectedRevision">Revision parsed from <c>If-Match</c>, or <see langword="null"/> when absent.</param>
    /// <param name="command">The patch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated document or a <c>400</c>/<c>404</c> failure.</returns>
    public async Task<Result<Document>> PatchAsync(
        Guid documentId,
        long? expectedRevision,
        PatchDocumentCommand command,
        CancellationToken cancellationToken)
    {
        if (expectedRevision is null)
        {
            return new Failure("400-if-match-required", "The If-Match header is required.");
        }

        var document = await documents.GetAsync(documentId, cancellationToken);
        if (document is null)
        {
            return NotFound();
        }

        if (document.Revision != expectedRevision)
        {
            return StalePrecondition();
        }

        var patch = await ToPatchAsync(command, cancellationToken);
        if (patch.IsFailure)
        {
            return patch.Failure;
        }

        var previousRevision = document.Revision;
        var applied = document.ApplyPatch(patch.Value, clock.UtcNow);
        if (applied.IsFailure)
        {
            return applied.Failure;
        }

        return await documents.UpdateAsync(document, previousRevision, cancellationToken) ? document : StalePrecondition();
    }

    private async Task<Result<DocumentPatch>> ToPatchAsync(PatchDocumentCommand command, CancellationToken cancellationToken)
    {
        var type = Optional<DocumentType?>.Unset;
        if (command.TypeId.IsSet)
        {
            var resolved = await ResolveTypeAsync(command.TypeId.Value, cancellationToken);
            if (resolved.IsFailure)
            {
                return resolved.Failure;
            }

            type = Optional<DocumentType?>.Of(resolved.Value);
        }

        var language = Optional<DocumentLanguage?>.Unset;
        if (command.LanguageId.IsSet)
        {
            var resolved = await ResolveLanguageAsync(command.LanguageId.Value, cancellationToken);
            if (resolved.IsFailure)
            {
                return resolved.Failure;
            }

            language = Optional<DocumentLanguage?>.Of(resolved.Value);
        }

        return new DocumentPatch
        {
            Title = command.Title,
            Description = command.Description,
            Type = type,
            Language = language,
            Tags = command.Tags,
            Metadata = command.Metadata,
        };
    }

    // A missing id is left to the domain ("type is required"); an id that points nowhere is a client error here.
    private async Task<Result<DocumentType?>> ResolveTypeAsync(Guid? typeId, CancellationToken cancellationToken)
    {
        if (typeId is null)
        {
            return (DocumentType?)null;
        }

        var type = await documentTypes.GetAsync(typeId.Value, cancellationToken);
        return type is null
            ? new Failure("400-document-type-not-found", "typeId does not reference an existing document type.")
            : type;
    }

    private async Task<Result<DocumentLanguage?>> ResolveLanguageAsync(Guid? languageId, CancellationToken cancellationToken)
    {
        if (languageId is null)
        {
            return (DocumentLanguage?)null;
        }

        var language = await documentLanguages.GetAsync(languageId.Value, cancellationToken);
        return language is null
            ? new Failure("400-document-language-not-found", "languageId does not reference an existing document language.")
            : language;
    }

    private static DocumentFilter Normalize(DocumentFilter filter) => new(
        filter.TypeId,
        filter.LanguageId,
        string.IsNullOrWhiteSpace(filter.Tag) ? null : filter.Tag,
        string.IsNullOrWhiteSpace(filter.Title) ? null : filter.Title.Trim());

    private static Failure NotFound() => new("404-document-not-found", "Document not found.");

    private static Failure StalePrecondition() =>
        new("400-etag-mismatch", "The If-Match ETag does not match the current revision.");
}
