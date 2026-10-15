using BlogDoFT.Libs.ResultPattern;
using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Application.Common;
using DocRag.Ingestion.Application.Idempotency;
using DocRag.Ingestion.Domain.Versions;

namespace DocRag.Ingestion.Application.Versions;

/// <summary>Input of <see cref="VersionService.CreateAsync"/>.</summary>
/// <param name="DocumentId">Parent document public identifier.</param>
/// <param name="IdempotencyKey">Client key, if any.</param>
/// <param name="Label">Editorial label.</param>
/// <param name="PublishedAt">Publication date, if known.</param>
/// <param name="EffectiveFrom">Start of validity, if known.</param>
/// <param name="EffectiveTo">End of validity, if known.</param>
public sealed record CreateVersionCommand(
    Guid DocumentId,
    string? IdempotencyKey,
    string? Label,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo);

/// <summary>A page of versions.</summary>
/// <param name="Items">The versions, newest first.</param>
/// <param name="NextCursor">Opaque cursor of the next page, or <see langword="null"/> on the last page.</param>
public sealed record VersionPage(IReadOnlyList<DocumentVersion> Items, string? NextCursor);

/// <summary>Use cases of document versions.</summary>
public sealed class VersionService(
    IDocumentRepository documents,
    IDocumentVersionRepository versions,
    IUnitOfWork unitOfWork,
    IdempotencyGate idempotency,
    IIdempotencyStore idempotencyStore,
    IClock clock)
{
    /// <summary>Default and maximum page sizes.</summary>
    public const int DefaultPageSize = Pagination.DefaultPageSize;

    /// <summary>Largest page size allowed.</summary>
    public const int MaxPageSize = Pagination.MaxPageSize;

    /// <summary>Registers a version of a document.</summary>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The version, or a <c>404</c>/<c>409</c>/<c>422</c> failure.</returns>
    public async Task<Result<DocumentVersion>> CreateAsync(CreateVersionCommand command, CancellationToken cancellationToken)
    {
        var document = await documents.GetAsync(command.DocumentId, cancellationToken);
        if (document is null)
        {
            return new Failure("404-document-not-found", "Document not found.");
        }

        var now = clock.UtcNow;
        var built = DocumentVersionBuilder.New()
            .WithPublicId(Guid.CreateVersion7(now))
            .ForDocument(document.Id, document.PublicId)
            .WithLabel(command.Label)
            .WithDates(command.PublishedAt, command.EffectiveFrom, command.EffectiveTo)
            .Build(now);
        if (built.IsFailure)
        {
            return built.Failure;
        }

        var version = built.Value;
        var operation = $"create-version:{document.PublicId}";
        var fingerprint = Fingerprint.Of(operation, version.Label, version.PublishedAt, version.EffectiveFrom, version.EffectiveTo);
        var request = idempotency.Describe(operation, command.IdempotencyKey, fingerprint);

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
                var original = await versions.GetAsync(
                    command.DocumentId, decision.ReplayResourceId!.Value, false, cancellationToken);
                return original is null ? new Failure("404-version-not-found", "The original version no longer exists.") : original;
            }

            var inserted = await versions.InsertAsync(version, cancellationToken);
            if (inserted.IsFailure)
            {
                return inserted.Failure;
            }

            if (request is not null)
            {
                await idempotencyStore.CompleteAsync(request, fingerprint, 201, version.PublicId, cancellationToken);
            }

            await unitOfWork.CommitAsync(cancellationToken);
        }

        return version;
    }

    /// <summary>Finds a version with its file and ingestion status.</summary>
    /// <param name="documentId">Parent document public identifier.</param>
    /// <param name="versionId">Version public identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The version or a <c>404</c> failure.</returns>
    public async Task<Result<DocumentVersion>> GetAsync(Guid documentId, Guid versionId, CancellationToken cancellationToken)
    {
        var version = await versions.GetAsync(documentId, versionId, false, cancellationToken);
        return version is null ? new Failure("404-version-not-found", "Version not found.") : version;
    }

    /// <summary>Lists versions newest first.</summary>
    /// <param name="documentId">Parent document public identifier.</param>
    /// <param name="pageSize">Requested page size (1 to 100).</param>
    /// <param name="cursor">Opaque cursor of the previous page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page, or a <c>400</c>/<c>404</c> failure.</returns>
    public async Task<Result<VersionPage>> ListAsync(
        Guid documentId,
        int pageSize,
        string? cursor,
        CancellationToken cancellationToken)
    {
        if (Pagination.Validate(pageSize) is { } invalidPageSize)
        {
            return invalidPageSize;
        }

        var after = Cursor.Decode(cursor);
        if (after.IsFailure)
        {
            return after.Failure;
        }

        var document = await documents.GetAsync(documentId, cancellationToken);
        if (document is null)
        {
            return new Failure("404-document-not-found", "Document not found.");
        }

        var rows = await versions.ListAsync(document.Id, pageSize + 1, after.Value, cancellationToken);
        var items = rows.Take(pageSize).ToList();
        return new VersionPage(items, rows.Count > pageSize ? Cursor.Encode(items[^1].PublicId) : null);
    }
}
