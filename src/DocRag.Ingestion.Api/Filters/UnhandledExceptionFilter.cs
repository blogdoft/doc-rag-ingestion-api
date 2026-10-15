using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DocRag.Ingestion.Api.Filters;

/// <summary>
/// Last-resort handler for exceptions escaping an action. Replies with a bare <c>500</c> so nothing
/// about the failure leaks to the client; the full exception goes to the application log. Client
/// disconnects are not errors.
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

        logger.LogError(context.Exception, "Unhandled exception while processing {Method} {Path}", context.HttpContext.Request.Method, context.HttpContext.Request.Path);
        context.Result = new StatusCodeResult(StatusCodes.Status500InternalServerError);
        context.ExceptionHandled = true;
    }
}
