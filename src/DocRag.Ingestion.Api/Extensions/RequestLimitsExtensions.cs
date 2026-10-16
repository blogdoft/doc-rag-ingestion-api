using DocRag.Ingestion.Application.Options;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Options;

namespace DocRag.Ingestion.Api.Extensions;

/// <summary>Request body size limits.</summary>
public static class RequestLimits
{
    /// <summary>Largest accepted body of a JSON endpoint (1 MiB).</summary>
    public const long JsonBodyBytes = 1024 * 1024;
}

/// <summary>Registers the request body size limits.</summary>
public static class RequestLimitsExtensions
{
    /// <summary>
    /// Sets Kestrel's global body limit to the configured upload maximum (<c>Upload:MaxBytes</c>); the JSON
    /// endpoints narrow it to <see cref="RequestLimits.JsonBodyBytes"/> with <c>[RequestSizeLimit]</c>.
    /// </summary>
    /// <remarks>
    /// A low global limit makes Kestrel close the connection after an early rejection (for example an expired
    /// token) of a body larger than that limit, because it cannot drain the unread body. A reverse proxy still
    /// sending the body then reports a 502 instead of the real 401/403. With the limit at the upload maximum,
    /// Kestrel drains the body, keeps the connection and the proxy relays the actual status.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddRequestBodyLimits(this IServiceCollection services)
    {
        services.AddOptions<KestrelServerOptions>()
            .Configure<IOptions<UploadOptions>>((kestrel, upload) => kestrel.Limits.MaxRequestBodySize = upload.Value.MaxBytes);
        return services;
    }
}
