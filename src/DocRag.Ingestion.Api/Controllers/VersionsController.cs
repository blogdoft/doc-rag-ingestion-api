using System.ComponentModel.DataAnnotations;
using DocRag.Ingestion.Api.Authentication;
using DocRag.Ingestion.Api.Contracts;
using DocRag.Ingestion.Api.Problems;
using DocRag.Ingestion.Application.Versions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DocRag.Ingestion.Api.Controllers;

/// <summary>Editorial versions of a document and their ingestion state.</summary>
[ApiController]
[ApiExplorerSettings(GroupName = "versions")]
[Route("api/v1/documents/{documentId:guid}/versions")]
public sealed class VersionsController(VersionService versions) : ControllerBase
{
    /// <summary>Register a version</summary>
    /// <remarks>
    /// Registers an edition identified by <c>label</c> (for example <c>4.00</c>). The pair
    /// (document, label) is unique. The version starts as <c>awaiting_upload</c>. Dates are
    /// stored exactly as sent and never inferred. Supports <c>Idempotency-Key</c>.
    /// </remarks>
    /// <param name="documentId">Parent document identifier.</param>
    /// <param name="request">The version to register.</param>
    /// <param name="idempotencyKey">Optional safe-retry key.</param>
    /// <param name="cancellationToken">Propagates request abort.</param>
    /// <response code="201">Version created.</response>
    /// <response code="400">Malformed JSON or invalid field; see <c>errors</c>.</response>
    /// <response code="401">Missing or invalid token. No body.</response>
    /// <response code="403">Token lacks the write scope. No body.</response>
    /// <response code="404">Document not found. No body.</response>
    /// <response code="409">The label already exists for this document, or the idempotency key conflicts.</response>
    /// <response code="422">Business rule violated (e.g. <c>effectiveTo</c> before <c>effectiveFrom</c>).</response>
    [HttpPost]
    [Authorize(AuthenticationExtensions.WritePolicy)]
    [Consumes("application/json")]
    [ProducesResponseType<VersionResponse>(StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<IActionResult> CreateAsync(
        Guid documentId,
        [FromBody] CreateVersionRequest request,
        [FromHeader(Name = "Idempotency-Key")][StringLength(128, MinimumLength = 1)] string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        var result = await versions.CreateAsync(
            new CreateVersionCommand(
                documentId,
                idempotencyKey,
                request.Label,
                request.PublishedAt,
                request.EffectiveFrom,
                request.EffectiveTo),
            cancellationToken);

        return result.IsFailure
            ? result.Failure.ToActionResult(HttpContext)
            : Created($"{Request.PathBase}/api/v1/documents/{documentId}/versions/{result.Value.PublicId}", VersionResponse.From(result.Value));
    }

    /// <summary>List versions</summary>
    /// <remarks>Newest first, with opaque cursor pagination: pass the returned <c>nextCursor</c> as <c>cursor</c> to get the next page.</remarks>
    /// <param name="documentId">Parent document identifier.</param>
    /// <param name="pageSize">Maximum items per page, 1 to 100 (default 20).</param>
    /// <param name="cursor">Opaque cursor from the previous page.</param>
    /// <param name="cancellationToken">Propagates request abort.</param>
    /// <response code="200">A page of versions.</response>
    /// <response code="400">Invalid <c>pageSize</c> or <c>cursor</c>.</response>
    /// <response code="401">Missing or invalid token. No body.</response>
    /// <response code="403">Token lacks the read scope. No body.</response>
    /// <response code="404">Document not found. No body.</response>
    [HttpGet]
    [Authorize(AuthenticationExtensions.ReadPolicy)]
    [ProducesResponseType<VersionPageResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListAsync(
        Guid documentId,
        [FromQuery] int pageSize = VersionService.DefaultPageSize,
        [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var result = await versions.ListAsync(documentId, pageSize, cursor, cancellationToken);
        return result.IsFailure ? result.Failure.ToActionResult(HttpContext) : Ok(VersionPageResponse.From(result.Value));
    }

    /// <summary>Get a version and its ingestion state</summary>
    /// <remarks>Returns the version and its original file, if received. <c>uploaded</c> only confirms that ingestion finished, not any downstream processing.</remarks>
    /// <param name="documentId">Parent document identifier.</param>
    /// <param name="versionId">Version identifier.</param>
    /// <param name="cancellationToken">Propagates request abort.</param>
    /// <response code="200">The version.</response>
    /// <response code="401">Missing or invalid token. No body.</response>
    /// <response code="403">Token lacks the read scope. No body.</response>
    /// <response code="404">Not found. No body.</response>
    [HttpGet("{versionId:guid}")]
    [Authorize(AuthenticationExtensions.ReadPolicy)]
    [ProducesResponseType<VersionResponse>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid documentId, Guid versionId, CancellationToken cancellationToken)
    {
        var result = await versions.GetAsync(documentId, versionId, cancellationToken);
        return result.IsFailure ? result.Failure.ToActionResult(HttpContext) : Ok(VersionResponse.From(result.Value));
    }
}
