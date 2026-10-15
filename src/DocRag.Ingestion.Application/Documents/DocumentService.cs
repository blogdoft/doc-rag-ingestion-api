using System.Text.Json;
using BlogDoFT.Libs.ResultPattern;
using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Application.Common;
using DocRag.Ingestion.Application.Idempotency;
using DocRag.Ingestion.Domain.Documents;

namespace DocRag.Ingestion.Application.Documents;

/// <summary>Input of <see cref="DocumentService.CreateAsync"/>.</summary>
/// <param name="IdempotencyKey">Client key, if any.</param>
/// <param name="Title">Title.</param>
/// <param name="Description">Description.</param>
/// <param name="Type">Category.</param>
/// <param name="Language">BCP 47 tag.</param>
/// <param name="Tags">Tags.</param>
/// <param name="Metadata">Free-form JSON object.</param>
public sealed record CreateDocumentCommand(
    string? IdempotencyKey,
    string? Title,
    string? Description,
    string? Type,
    string? Language,
    IReadOnlyList<string>? Tags,
    JsonElement? Metadata);

/// <summary>Optional exact-match filters of a document listing; a <see langword="null"/> member is not applied.</summary>
/// <param name="Type">Category.</param>
/// <param name="Language">BCP 47 tag.</param>
/// <param name="Tag">A tag the document must carry.</param>
/// <param name="Title">
/// Case-insensitive title search. <c>*</c> matches any run of characters; without any <c>*</c> the value
/// must equal the whole title.
/// </param>
public sealed record DocumentFilter(string? Type = null, string? Language = null, string? Tag = null, string? Title = null);

/// <summary>A page of documents.</summary>
/// <param name="Items">The documents, newest first.</param>
/// <param name="NextCursor">Opaque cursor of the next page, or <see langword="null"/> on the last page.</param>
public sealed record DocumentPage(IReadOnlyList<Document> Items, string? NextCursor);

/// <summary>Use cases of the document aggregate.</summary>
public sealed class DocumentService(
    IDocumentRepository documents,
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
        var built = DocumentBuilder.New()
            .WithPublicId(Guid.CreateVersion7(now))
            .WithTitle(command.Title)
            .WithDescription(command.Description)
            .OfType(command.Type)
            .InLanguage(command.Language)
            .WithTags(command.Tags)
            .WithMetadata(command.Metadata)
            .Build(now);
        if (built.IsFailure)
        {
            return built.Failure;
        }

        var document = built.Value;
        var fingerprint = Fingerprint.Of(
            CreateOperation, document.Title, document.Description, document.Type, document.Language, document.Tags, document.Metadata);
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
    /// <param name="patch">The patch.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated document or a <c>404</c>/<c>412</c>/<c>422</c>/<c>428</c> failure.</returns>
    public async Task<Result<Document>> PatchAsync(
        Guid documentId,
        long? expectedRevision,
        DocumentPatch patch,
        CancellationToken cancellationToken)
    {
        if (expectedRevision is null)
        {
            return new Failure("428-if-match-required", "The If-Match header is required.");
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

        var previousRevision = document.Revision;
        var applied = document.ApplyPatch(patch, clock.UtcNow);
        if (applied.IsFailure)
        {
            return applied.Failure;
        }

        return await documents.UpdateAsync(document, previousRevision, cancellationToken) ? document : StalePrecondition();
    }

    private static DocumentFilter Normalize(DocumentFilter filter) => new(
        string.IsNullOrWhiteSpace(filter.Type) ? null : filter.Type,
        string.IsNullOrWhiteSpace(filter.Language) ? null : filter.Language,
        string.IsNullOrWhiteSpace(filter.Tag) ? null : filter.Tag,
        string.IsNullOrWhiteSpace(filter.Title) ? null : filter.Title.Trim());

    private static Failure NotFound() => new("404-document-not-found", "Document not found.");

    private static Failure StalePrecondition() =>
        new("412-etag-mismatch", "The If-Match ETag does not match the current revision.");
}
