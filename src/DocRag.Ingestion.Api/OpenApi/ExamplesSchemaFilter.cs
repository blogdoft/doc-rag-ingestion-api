using System.Text.Json.Nodes;
using DocRag.Ingestion.Api.Contracts;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace DocRag.Ingestion.Api.OpenApi;

/// <summary>Attaches realistic example payloads to the request and response schemas shown in Swagger UI.</summary>
internal sealed class ExamplesSchemaFilter : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema is not OpenApiSchema concrete)
        {
            return;
        }

        if (context.Type == typeof(CreateDocumentRequest))
        {
            concrete.Example = new JsonObject
            {
                ["title"] = "Manual de Orientações do Contribuinte - CT-e",
                ["description"] = "Manual técnico de orientação do CT-e",
                ["type"] = "fiscal-manual",
                ["language"] = "pt-BR",
                ["tags"] = new JsonArray("cte", "sefaz", "documento-fiscal"),
                ["metadata"] = new JsonObject { ["publisher"] = "ENCAT", ["jurisdiction"] = "BR", ["documentModel"] = "57" },
            };
        }
        else if (context.Type == typeof(PatchDocumentRequest))
        {
            concrete.Example = new JsonObject
            {
                ["description"] = "Manual atualizado",
                ["tags"] = new JsonArray("cte", "sefaz"),
                ["metadata"] = new JsonObject { ["documentModel"] = null },
            };
        }
        else if (context.Type == typeof(CreateVersionRequest))
        {
            concrete.Example = new JsonObject
            {
                ["label"] = "4.00",
                ["publishedAt"] = null,
                ["effectiveFrom"] = null,
                ["effectiveTo"] = null,
            };
        }
        else if (context.Type == typeof(FileResponse))
        {
            concrete.Example = new JsonObject
            {
                ["id"] = "0199a8bb-7bc4-7000-8000-4d981eb52c93",
                ["fileName"] = "MOC_CTe_VisaoGeral_v4.00.pdf",
                ["contentType"] = "application/pdf",
                ["sizeBytes"] = 1048576,
                ["sha256"] = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
                ["status"] = "uploaded",
                ["uploadedAt"] = "2026-10-15T03:08:00Z",
            };
        }
    }
}
