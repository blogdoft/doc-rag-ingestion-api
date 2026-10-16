using DocRag.Ingestion.Api.Problems;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.WebUtilities;

namespace DocRag.Ingestion.Api.Filters;

/// <summary>
/// Last-resort handler for exceptions escaping an action. Replies with a bare <c>500</c> so nothing
/// about the failure leaks to the client; the full exception goes to the application log. Client
/// disconnects are not errors, and a 4xx <see cref="BadHttpRequestException"/> raised by Kestrel while
/// the body is read (for example <c>413</c> for a JSON body above the endpoint limit) is a client error
/// answered with that status as a problem document.
/// </summary>
public sealed class UnhandledExceptionFilter(ILogger<UnhandledExceptionFilter> logger) : IExceptionFilter
{
    /// <inheritdoc />
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is OperationCanceledException && context.HttpContext.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation("Request {Method} {Path} was cancelled by the client", context.HttpContext.Request.Method, context.HttpContext.Request.Path);
            context.Result = new StatusCodeResult(StatusCodes.Status499ClientClosedRequest);
            context.ExceptionHandled = true;
            return;
        }

        if (context.Exception is BadHttpRequestException { StatusCode: >= StatusCodes.Status400BadRequest and < StatusCodes.Status500InternalServerError } badRequest)
        {
            logger.LogInformation("Request {Method} {Path} rejected with {Status}: {Reason}", context.HttpContext.Request.Method, context.HttpContext.Request.Path, badRequest.StatusCode, badRequest.Message);
            context.Result = ProblemResults.Build(badRequest.StatusCode, ReasonPhrases.GetReasonPhrase(badRequest.StatusCode), badRequest.Message, context.HttpContext);
            context.ExceptionHandled = true;
            return;
        }

        logger.LogError(context.Exception, "Unhandled exception while processing {Method} {Path}", context.HttpContext.Request.Method, context.HttpContext.Request.Path);
        context.Result = new StatusCodeResult(StatusCodes.Status500InternalServerError);
        context.ExceptionHandled = true;
    }
}
