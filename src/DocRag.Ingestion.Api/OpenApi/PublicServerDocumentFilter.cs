using DocRag.Ingestion.Api.Extensions;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace DocRag.Ingestion.Api.OpenApi;

/// <summary>
/// Publishes the externally reachable base URL (<c>PublicBaseUrl</c>) as the document's only server,
/// so Swagger UI's "Try it out" works when the API sits behind a path-stripping reverse proxy.
/// Without the setting the UI falls back to the current request's own scheme and host.
/// </summary>
internal sealed class PublicServerDocumentFilter(IConfiguration configuration) : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        var publicBaseUrl = configuration[SwaggerExtensions.PublicBaseUrlKey];
        if (string.IsNullOrWhiteSpace(publicBaseUrl))
        {
            return;
        }

        swaggerDoc.Servers = [new OpenApiServer { Url = publicBaseUrl.TrimEnd('/') }];
    }
}
