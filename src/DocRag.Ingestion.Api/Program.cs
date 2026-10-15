using BlogDoFT.Libs.WarmUp.Extensions;
using DocRag.Ingestion.Api.Authentication;
using DocRag.Ingestion.Api.Extensions;
using DocRag.Ingestion.Api.Middleware;
using DocRag.Ingestion.Application;
using DocRag.Ingestion.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Body limits: 1 MiB for JSON endpoints; the upload endpoint lifts it itself (StreamingUploadFilter).
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 1024 * 1024);

builder.AddObservability("doc-rag-ingestion-api");

builder.Services
    .AddIngestionAuthentication(builder.Configuration)
    .AddApiControllers()
    .AddSwaggerDocumentation()
    .AddApplicationServices(builder.Configuration)
    .AddPostgresPersistence(builder.Configuration)
    .AddGarageStorage(builder.Configuration)
    .AddDatabaseMigrations(builder.Configuration)
    .AddReadinessChecks()
    .AddWarmUp();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseClientErrorProblems();
app.UseSwaggerDocumentation();
app.UseObservability();
app.MapHealthProbes();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

await app.RunAsync();

/// <summary>Entry point marker so <c>WebApplicationFactory&lt;Program&gt;</c> can bootstrap this API in tests.</summary>
public partial class Program
{
    protected Program()
    {
    }
}
