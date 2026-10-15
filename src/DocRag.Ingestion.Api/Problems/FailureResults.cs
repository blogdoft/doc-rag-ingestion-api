using BlogDoFT.Libs.ResultPattern;
using DocRag.Ingestion.Api.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace DocRag.Ingestion.Api.Problems;

/// <summary>
/// Translates an application <see cref="Failure"/> into an HTTP result in one central place. The
/// intended status is the leading number of <see cref="Failure.Code"/> (e.g. <c>409-upload-in-progress</c>).
/// </summary>
public static class FailureResults
{
    /// <summary>Maps the failure to a status code and, where allowed, a problem body.</summary>
    /// <param name="failure">The failure.</param>
    /// <param name="context">The current request.</param>
    /// <returns>The HTTP result.</returns>
    public static IActionResult ToActionResult(this Failure failure, HttpContext context)
    {
        var status = ParseStatus(failure.Code);

        // 401, 403, 404 and 5xx carry no body: the outcome is only recorded in the application log.
        if (status is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden or StatusCodes.Status404NotFound or >= 500)
        {
            Log(failure, status, context);
            return new StatusCodeResult(status);
        }

        return ProblemResults.Build(status, ReasonPhrases.GetReasonPhrase(status), failure.Message, context);
    }

    private static void Log(Failure failure, int status, HttpContext context)
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(FailureResults).FullName!);
        if (status == StatusCodes.Status403Forbidden)
        {
            logger.LogWarning(
                "Request {Method} {Path} forbidden for {Username} ({FailureCode})",
                context.Request.Method, context.Request.Path, context.User.GetUsername(), failure.Code);
            return;
        }

        logger.Log(
            status >= 500 ? LogLevel.Error : LogLevel.Information,
            "Request {Method} {Path} failed with {Status} ({FailureCode}): {FailureMessage}",
            context.Request.Method, context.Request.Path, status, failure.Code, failure.Message);
    }

    private static int ParseStatus(string code)
    {
        var separator = code.IndexOf('-', StringComparison.Ordinal);
        return int.TryParse(separator > 0 ? code[..separator] : code, out var status) ? status : StatusCodes.Status400BadRequest;
    }
}
