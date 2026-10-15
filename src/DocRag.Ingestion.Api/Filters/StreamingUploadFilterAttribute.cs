using DocRag.Ingestion.Application.Options;
using DocRag.Ingestion.Api.Problems;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core.Features;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace DocRag.Ingestion.Api.Filters;

/// <summary>
/// Prepares an endpoint that streams a raw request body: removes Kestrel's body-size limit (the
/// application enforces the configured upload limit itself, as a clean 413 problem), disables the
/// minimum data rate so slow clients are not cut off, and rejects an oversized declared
/// <c>Content-Length</c> before any work is done.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class StreamingUploadFilterAttribute : Attribute, IAsyncActionFilter
{
    /// <inheritdoc />
    public Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var maxBytes = http.RequestServices.GetRequiredService<IOptions<UploadOptions>>().Value.MaxBytes;

        if (http.Request.ContentLength > maxBytes)
        {
            context.Result = ProblemResults.Build(
                StatusCodes.Status413PayloadTooLarge,
                "Payload Too Large",
                $"The upload exceeds the limit of {maxBytes} bytes.",
                http);
            return Task.CompletedTask;
        }

        var sizeFeature = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false })
        {
            sizeFeature.MaxRequestBodySize = null;
        }

        var dataRate = http.Features.Get<IHttpMinRequestBodyDataRateFeature>();
        if (dataRate is not null)
        {
            dataRate.MinDataRate = null;
        }

        return next();
    }
}
