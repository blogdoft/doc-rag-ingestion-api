using BlogDoFT.Libs.ResultPattern;
using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Application.Common;
using DocRag.Ingestion.Domain.MasterData;

namespace DocRag.Ingestion.Application.MasterData;

/// <summary>Persistence of <see cref="DocumentType"/>.</summary>
public interface IDocumentTypeRepository
{
    /// <summary>Inserts a document type and assigns its internal key.</summary>
    /// <param name="documentType">The document type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success, or a <c>400</c> failure when the name is already taken.</returns>
    Task<Result> InsertAsync(DocumentType documentType, CancellationToken cancellationToken = default);

    /// <summary>Finds a document type.</summary>
    /// <param name="publicId">Public identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The document type or <see langword="null"/>.</returns>
    Task<DocumentType?> GetAsync(Guid publicId, CancellationToken cancellationToken = default);

    /// <summary>Lists document types newest first, keyset-paginated by public identifier.</summary>
    /// <param name="limit">Maximum rows to return.</param>
    /// <param name="after">Only rows older than this public identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The document types.</returns>
    Task<IReadOnlyList<DocumentType>> ListAsync(int limit, Guid? after, CancellationToken cancellationToken = default);

    /// <summary>Persists the changed name.</summary>
    /// <param name="documentType">The changed document type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success, or a <c>400</c> failure when the name is already taken.</returns>
    Task<Result> UpdateAsync(DocumentType documentType, CancellationToken cancellationToken = default);

    /// <summary>Removes a document type.</summary>
    /// <param name="publicId">Public identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="false"/> when no such document type exists.</returns>
    Task<bool> DeleteAsync(Guid publicId, CancellationToken cancellationToken = default);
}

/// <summary>A page of document types.</summary>
/// <param name="Items">The document types, newest first.</param>
/// <param name="NextCursor">Opaque cursor of the next page, or <see langword="null"/> on the last page.</param>
public sealed record DocumentTypePage(IReadOnlyList<DocumentType> Items, string? NextCursor);

/// <summary>Use cases of the document type master data.</summary>
public sealed class DocumentTypeService(IDocumentTypeRepository documentTypes, IClock clock)
{
    /// <summary>Registers a document type.</summary>
    /// <param name="name">The category name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The document type, or a <c>400</c> failure.</returns>
    public async Task<Result<DocumentType>> CreateAsync(string? name, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var built = DocumentType.Builder().WithPublicId(Guid.CreateVersion7(now)).Named(name).Build(now);
        if (built.IsFailure)
        {
            return built.Failure;
        }

        var inserted = await documentTypes.InsertAsync(built.Value, cancellationToken);
        return inserted.IsFailure ? inserted.Failure : built.Value;
    }

    /// <summary>Finds a document type.</summary>
    /// <param name="documentTypeId">Public identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The document type or a <c>404</c> failure.</returns>
    public async Task<Result<DocumentType>> GetAsync(Guid documentTypeId, CancellationToken cancellationToken)
    {
        var documentType = await documentTypes.GetAsync(documentTypeId, cancellationToken);
        return documentType is null ? NotFound() : documentType;
    }

    /// <summary>Lists document types newest first.</summary>
    /// <param name="pageSize">Requested page size (1 to 100).</param>
    /// <param name="cursor">Opaque cursor of the previous page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page, or a <c>400</c> failure.</returns>
    public async Task<Result<DocumentTypePage>> ListAsync(int pageSize, string? cursor, CancellationToken cancellationToken)
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

        var rows = await documentTypes.ListAsync(pageSize + 1, after.Value, cancellationToken);
        var items = rows.Take(pageSize).ToList();
        return new DocumentTypePage(items, rows.Count > pageSize ? Cursor.Encode(items[^1].PublicId) : null);
    }

    /// <summary>Renames a document type.</summary>
    /// <param name="documentTypeId">Public identifier.</param>
    /// <param name="name">The new name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated document type, or a <c>400</c>/<c>404</c> failure.</returns>
    public async Task<Result<DocumentType>> UpdateAsync(Guid documentTypeId, string? name, CancellationToken cancellationToken)
    {
        var documentType = await documentTypes.GetAsync(documentTypeId, cancellationToken);
        if (documentType is null)
        {
            return NotFound();
        }

        var renamed = documentType.Rename(name, clock.UtcNow);
        if (renamed.IsFailure)
        {
            return renamed.Failure;
        }

        var updated = await documentTypes.UpdateAsync(documentType, cancellationToken);
        return updated.IsFailure ? updated.Failure : documentType;
    }

    /// <summary>Removes a document type.</summary>
    /// <param name="documentTypeId">Public identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success, or a <c>404</c> failure.</returns>
    public async Task<Result> DeleteAsync(Guid documentTypeId, CancellationToken cancellationToken) =>
        await documentTypes.DeleteAsync(documentTypeId, cancellationToken) ? Result.AsSuccess() : NotFound();

    private static Failure NotFound() => new("404-document-type-not-found", "Document type not found.");
}
