using DocRag.Ingestion.Api.Problems;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DocRag.Ingestion.Api.Filters;

/// <summary>
/// MVC rejects a wrong <c>Content-Type</c> (<c>415</c>) or an unsatisfiable <c>Accept</c> (<c>406</c>)
/// with a bare status. These are client errors that must carry the standard problem body, so they
/// are rewritten here, in one place.
/// </summary>
public sealed class MvcRejectionProblemFilter : IAlwaysRunResultFilter
{
    /// <inheritdoc />
    public void OnResultExecuting(ResultExecutingContext context)
    {
        context.Result = context.Result switch
        {
            UnsupportedMediaTypeResult => ProblemResults.Build(
                StatusCodes.Status415UnsupportedMediaType,
                "Unsupported Media Type",
                "The Content-Type of the request is not accepted by this operation.",
                context.HttpContext),
            StatusCodeResult { StatusCode: StatusCodes.Status406NotAcceptable } => ProblemResults.Build(
                StatusCodes.Status406NotAcceptable,
                "Not Acceptable",
                "The server cannot produce a response matching the Accept header.",
                context.HttpContext),
            _ => context.Result,
        };
    }

    /// <inheritdoc />
    public void OnResultExecuted(ResultExecutedContext context)
    {
    }
}
