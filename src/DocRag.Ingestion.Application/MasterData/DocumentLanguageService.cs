using BlogDoFT.Libs.ResultPattern;
using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Application.Common;
using DocRag.Ingestion.Domain.MasterData;

namespace DocRag.Ingestion.Application.MasterData;

/// <summary>Persistence of <see cref="DocumentLanguage"/>.</summary>
public interface IDocumentLanguageRepository
{
    /// <summary>Inserts a document language and assigns its internal key.</summary>
    /// <param name="documentLanguage">The document language.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success, or a <c>400</c> failure when the code is already taken.</returns>
    Task<Result> InsertAsync(DocumentLanguage documentLanguage, CancellationToken cancellationToken = default);

    /// <summary>Finds a document language.</summary>
    /// <param name="publicId">Public identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The document language or <see langword="null"/>.</returns>
    Task<DocumentLanguage?> GetAsync(Guid publicId, CancellationToken cancellationToken = default);

    /// <summary>Lists document languages newest first, keyset-paginated by public identifier.</summary>
    /// <param name="limit">Maximum rows to return.</param>
    /// <param name="after">Only rows older than this public identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The document languages.</returns>
    Task<IReadOnlyList<DocumentLanguage>> ListAsync(int limit, Guid? after, CancellationToken cancellationToken = default);

    /// <summary>Persists the changed code.</summary>
    /// <param name="documentLanguage">The changed document language.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success, or a <c>400</c> failure when the code is already taken.</returns>
    Task<Result> UpdateAsync(DocumentLanguage documentLanguage, CancellationToken cancellationToken = default);

    /// <summary>Removes a document language.</summary>
    /// <param name="publicId">Public identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="false"/> when no such document language exists.</returns>
    Task<bool> DeleteAsync(Guid publicId, CancellationToken cancellationToken = default);
}

/// <summary>A page of document languages.</summary>
/// <param name="Items">The document languages, newest first.</param>
/// <param name="NextCursor">Opaque cursor of the next page, or <see langword="null"/> on the last page.</param>
public sealed record DocumentLanguagePage(IReadOnlyList<DocumentLanguage> Items, string? NextCursor);

/// <summary>Use cases of the document language master data.</summary>
public sealed class DocumentLanguageService(IDocumentLanguageRepository documentLanguages, IClock clock)
{
    /// <summary>Registers a document language.</summary>
    /// <param name="code">The BCP 47 language tag.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The document language, or a <c>400</c> failure.</returns>
    public async Task<Result<DocumentLanguage>> CreateAsync(string? code, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var built = DocumentLanguage.Builder().WithPublicId(Guid.CreateVersion7(now)).WithCode(code).Build(now);
        if (built.IsFailure)
        {
            return built.Failure;
        }

        var inserted = await documentLanguages.InsertAsync(built.Value, cancellationToken);
        return inserted.IsFailure ? inserted.Failure : built.Value;
    }

    /// <summary>Finds a document language.</summary>
    /// <param name="documentLanguageId">Public identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The document language or a <c>404</c> failure.</returns>
    public async Task<Result<DocumentLanguage>> GetAsync(Guid documentLanguageId, CancellationToken cancellationToken)
    {
        var documentLanguage = await documentLanguages.GetAsync(documentLanguageId, cancellationToken);
        return documentLanguage is null ? NotFound() : documentLanguage;
    }

    /// <summary>Lists document languages newest first.</summary>
    /// <param name="pageSize">Requested page size (1 to 100).</param>
    /// <param name="cursor">Opaque cursor of the previous page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page, or a <c>400</c> failure.</returns>
    public async Task<Result<DocumentLanguagePage>> ListAsync(int pageSize, string? cursor, CancellationToken cancellationToken)
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

        var rows = await documentLanguages.ListAsync(pageSize + 1, after.Value, cancellationToken);
        var items = rows.Take(pageSize).ToList();
        return new DocumentLanguagePage(items, rows.Count > pageSize ? Cursor.Encode(items[^1].PublicId) : null);
    }

    /// <summary>Changes the BCP 47 tag of a document language.</summary>
    /// <param name="documentLanguageId">Public identifier.</param>
    /// <param name="code">The new BCP 47 tag.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated document language, or a <c>400</c>/<c>404</c> failure.</returns>
    public async Task<Result<DocumentLanguage>> UpdateAsync(Guid documentLanguageId, string? code, CancellationToken cancellationToken)
    {
        var documentLanguage = await documentLanguages.GetAsync(documentLanguageId, cancellationToken);
        if (documentLanguage is null)
        {
            return NotFound();
        }

        var changed = documentLanguage.ChangeCode(code, clock.UtcNow);
        if (changed.IsFailure)
        {
            return changed.Failure;
        }

        var updated = await documentLanguages.UpdateAsync(documentLanguage, cancellationToken);
        return updated.IsFailure ? updated.Failure : documentLanguage;
    }

    /// <summary>Removes a document language.</summary>
    /// <param name="documentLanguageId">Public identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success, or a <c>404</c> failure.</returns>
    public async Task<Result> DeleteAsync(Guid documentLanguageId, CancellationToken cancellationToken) =>
        await documentLanguages.DeleteAsync(documentLanguageId, cancellationToken) ? Result.AsSuccess() : NotFound();

    private static Failure NotFound() => new("404-document-language-not-found", "Document language not found.");
}
