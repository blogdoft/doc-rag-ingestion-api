using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DocRag.Ingestion.Api.Problems;

/// <summary>Builds RFC 9457 <c>application/problem+json</c> results.</summary>
public static class ProblemResults
{
    /// <summary>Builds a <c>400</c> from model binding/validation errors, naming each rejected field.</summary>
    /// <param name="modelState">The failed model state.</param>
    /// <param name="httpContext">The current request.</param>
    /// <returns>The result.</returns>
    public static IActionResult BadRequestValidation(ModelStateDictionary modelState, HttpContext httpContext)
    {
        // When the body itself failed to deserialize, the "request is required" companion error is noise.
        var hasBodyError = modelState.Keys.Any(key => key.StartsWith('$'));
        var errors = modelState
            .Where(entry => entry.Value?.Errors.Count > 0 && !(hasBodyError && entry.Key is "request" or ""))
            .ToDictionary(
                entry => ToCamelCasePath(entry.Key),
                entry => entry.Value!.Errors
                    .Select(error => string.IsNullOrEmpty(error.ErrorMessage) ? "The input was not valid." : error.ErrorMessage)
                    .ToArray());

        var problem = Create(
            StatusCodes.Status400BadRequest,
            "Bad Request",
            "The request is malformed or invalid - see 'errors' for which field(s) and why.",
            httpContext);
        problem.Extensions["errors"] = errors;
        return ToResult(problem);
    }

    /// <summary>Builds a problem result for a failure with a body (anything except 401, 403, 404 and 5xx).</summary>
    /// <param name="status">HTTP status.</param>
    /// <param name="title">Short title.</param>
    /// <param name="detail">Human readable detail.</param>
    /// <param name="httpContext">The current request.</param>
    /// <returns>The result.</returns>
    public static IActionResult Build(int status, string title, string detail, HttpContext httpContext) =>
        ToResult(Create(status, title, detail, httpContext));

    private static ProblemDetails Create(int status, string title, string detail, HttpContext httpContext)
    {
        var problem = new ProblemDetails
        {
            Type = $"https://httpstatuses.io/{status}",
            Title = title,
            Status = status,
            Detail = detail,
            Instance = httpContext.Request.Path,
        };
        problem.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? httpContext.TraceIdentifier;
        return problem;
    }

    private static ObjectResult ToResult(ProblemDetails problem) => new(problem)
    {
        StatusCode = problem.Status,
        ContentTypes = { "application/problem+json" },
    };

    // ModelState keys are C# member paths for attribute failures but JSON paths ("$.title") for
    // deserialization failures; the wire format is camelCase, so the first kind is renamed.
    private static string ToCamelCasePath(string key) => key.StartsWith('$')
        ? key
        : string.Join('.', key.Split('.').Select(segment => JsonNamingPolicy.CamelCase.ConvertName(segment)));
}
