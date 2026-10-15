using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DocRag.Ingestion.Api.Middleware;

/// <summary>
/// Accepts or generates <c>X-Correlation-Id</c>, echoes it, tags the current trace and opens a log
/// scope so every line of the request carries it.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    /// <summary>Header name.</summary>
    public const string HeaderName = "X-Correlation-Id";

    private const string ItemKey = "CorrelationId";

    /// <summary>Reads the correlation identifier established for the request.</summary>
    /// <param name="context">The current request.</param>
    /// <returns>The identifier, or <see langword="null"/> before the middleware ran.</returns>
    public static string? Get(HttpContext context) => context.Items[ItemKey] as string;

    /// <summary>Runs the middleware.</summary>
    /// <param name="context">The current request.</param>
    /// <returns>A task completing when the rest of the pipeline is done.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers[HeaderName].FirstOrDefault();
        var correlationId = supplied is not null && Allowed().IsMatch(supplied)
            ? supplied
            : Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");

        context.Items[ItemKey] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });
        Activity.Current?.SetTag("correlation.id", correlationId);

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9._:\-]{1,128}$")]
    private static partial Regex Allowed();
}
