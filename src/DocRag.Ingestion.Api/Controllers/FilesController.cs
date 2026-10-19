using System.ComponentModel.DataAnnotations;
using DocRag.Ingestion.Api.Authentication;
using DocRag.Ingestion.Api.Contracts;
using DocRag.Ingestion.Api.Filters;
using DocRag.Ingestion.Api.Middleware;
using DocRag.Ingestion.Api.Problems;
using DocRag.Ingestion.Application.Files;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace DocRag.Ingestion.Api.Controllers;

/// <summary>Streaming reception of the original file of a version.</summary>
[ApiController]
[ApiExplorerSettings(GroupName = "files")]
[Route("api/v1/documents/{documentId:guid}/versions/{versionId:guid}/files")]
public sealed class FilesController(FileUploadService uploads) : ControllerBase
{
    /// <summary>Upload the original file (streaming)</summary>
    /// <remarks>
    /// Send the **raw bytes** as the request body: no multipart, no Base64. The body may use chunked
    /// transfer encoding (no <c>Content-Length</c>); the server reads it incrementally with bounded
    /// memory while computing the SHA-256 and byte count.
    ///
    /// <b>Headers.</b> <c>Content-Type</c> must be an enabled type (PDF, Markdown, plain text, HTML, DOCX) and is
    /// verified against the file's real signature. <c>Content-Disposition: attachment; filename="manual.pdf"</c>
    /// carries the original name; it is sanitized and never used as a storage key. Optional
    /// <c>X-Content-SHA256</c> (64 hex characters) must match the computed digest.
    ///
    /// <b>Concurrency.</b> Only one upload may be active per version (<c>400</c> otherwise) and a version with a
    /// completed file cannot be replaced (<c>400</c>). The service runs a limited number of uploads at once
    /// (<c>429</c>).
    ///
    /// <b>Retries.</b> Use <c>Idempotency-Key</c>. Repeating a completed upload with the same bytes returns the original
    /// result and publishes no second event; different bytes return <c>400</c>. An interrupted upload restarts from
    /// the beginning (no resumable uploads).
    ///
    /// <b>Success.</b> <c>201</c> means the file is stored, registered and its <c>DocumentUploaded</c> event is in the
    /// transactional outbox. It does <b>not</b> mean Kafka delivered it or that conversion/indexing finished.
    /// </remarks>
    /// <param name="documentId">Parent document identifier.</param>
    /// <param name="versionId">Version identifier.</param>
    /// <param name="idempotencyKey">Optional safe-retry key.</param>
    /// <param name="checksumSha256">Optional expected SHA-256 in hexadecimal.</param>
    /// <param name="cancellationToken">Propagates request abort; a client disconnect cancels the transfer.</param>
    /// <response code="201">File stored and registered.</response>
    /// <response code="400">Invalid headers, empty upload, SHA-256 mismatch, size limit exceeded, media type not allowed or not matching the content, version already has a file, another upload is active, or the idempotency key conflicts.</response>
    /// <response code="401">Missing or invalid token. No body.</response>
    /// <response code="403">Token lacks the write scope. No body.</response>
    /// <response code="404">Document or version not found. No body.</response>
    /// <response code="429">The service reached its concurrent upload limit.</response>
    /// <response code="503">Object storage unavailable. No body.</response>
    [HttpPost]
    [Authorize(AuthenticationExtensions.WritePolicy)]
    [StreamingUploadFilter]
    [ProducesResponseType<FileResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> UploadAsync(
        Guid documentId,
        Guid versionId,
        [FromHeader(Name = "Idempotency-Key")][StringLength(128, MinimumLength = 1)] string? idempotencyKey,
        [FromHeader(Name = "X-Content-SHA256")][RegularExpression("^[a-fA-F0-9]{64}$", ErrorMessage = "X-Content-SHA256 must be 64 hexadecimal characters.")] string? checksumSha256,
        CancellationToken cancellationToken)
    {
        var mediaType = MediaTypeHeaderValue.TryParse(Request.ContentType, out var parsed) ? parsed.MediaType.Value : null;

        var result = await uploads.UploadAsync(
            new UploadFileCommand(
                documentId,
                versionId,
                idempotencyKey,
                ReadFileName(),
                mediaType ?? string.Empty,
                checksumSha256,
                CorrelationIdMiddleware.Get(HttpContext),
                Request.Body),
            cancellationToken);

        return result.IsFailure
            ? result.Failure.ToActionResult(HttpContext)
            : Created($"{Request.PathBase}/api/v1/documents/{documentId}/versions/{versionId}", FileResponse.From(result.Value));
    }

    private string? ReadFileName()
    {
        if (!ContentDispositionHeaderValue.TryParse(Request.Headers.ContentDisposition.ToString(), out var disposition))
        {
            return null;
        }

        var name = disposition.FileNameStar.HasValue ? disposition.FileNameStar.Value : disposition.FileName.Value;
        return name?.Trim('"');
    }
}
