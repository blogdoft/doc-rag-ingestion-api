using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace DocRag.Ingestion.Api.Extensions;

/// <summary>
/// Endpoint routing answers a non-matching <c>Content-Type</c> (<c>415</c>), method (<c>405</c>) or
/// <c>Accept</c> (<c>406</c>) with a bare status before MVC runs. These client errors must still
/// carry the standard problem body, so they are completed here.
/// </summary>
public static class ClientErrorProblemExtensions
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Adds <c>application/problem+json</c> bodies to bare 405, 406 and 415 responses.</summary>
    /// <param name="app">The web application.</param>
    /// <returns>The same <paramref name="app"/>, for chaining.</returns>
    public static WebApplication UseClientErrorProblems(this WebApplication app)
    {
        app.UseStatusCodePages(async context =>
        {
            var http = context.HttpContext;
            var status = http.Response.StatusCode;
            if (status is not (StatusCodes.Status405MethodNotAllowed or StatusCodes.Status406NotAcceptable or StatusCodes.Status415UnsupportedMediaType))
            {
                return;
            }

            var problem = new ProblemDetails
            {
                Type = $"https://httpstatuses.io/{status}",
                Title = ReasonPhrases.GetReasonPhrase(status),
                Status = status,
                Detail = status switch
                {
                    StatusCodes.Status415UnsupportedMediaType => "The Content-Type of the request is not accepted by this operation.",
                    StatusCodes.Status406NotAcceptable => "The server cannot produce a response matching the Accept header.",
                    _ => "The HTTP method is not allowed for this resource.",
                },
                Instance = http.Request.Path,
            };
            problem.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? http.TraceIdentifier;

            http.Response.ContentType = "application/problem+json";
            await http.Response.WriteAsync(JsonSerializer.Serialize(problem, Json));
        });

        return app;
    }
}
