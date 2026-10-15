using BlogDoFT.Libs.Api.OpenTelemetry.Extensions;
using DocRag.Ingestion.Application;
using DocRag.Ingestion.Application.Common;
using DocRag.Ingestion.Infrastructure;
using DocRag.Ingestion.Infrastructure.Health;
using DocRag.Ingestion.Worker.Workers;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
});

builder.Services.AddOtel(builder.Configuration);
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(builder.Configuration["OTEL_SERVICE_NAME"] ?? "doc-rag-ingestion-worker"))
    .WithMetrics(metrics => metrics.AddMeter(IngestionTelemetry.Name))
    .WithTracing(tracing => tracing.AddSource(IngestionTelemetry.Name).AddNpgsql());

builder.Services
    .AddApplicationServices(builder.Configuration)
    .AddBackgroundProcessing(builder.Configuration)
    .AddPostgresPersistence(builder.Configuration)
    .AddGarageStorage(builder.Configuration)
    .AddKafkaPublisher(builder.Configuration);

builder.Services.AddHostedService<OutboxDispatcherWorker>();
builder.Services.AddHostedService<ReconciliationWorker>();

// Kafka is deliberately not part of readiness: an unreachable broker only grows the outbox backlog
// (alerted through the outbox metrics) and must not take the pod out of rotation.
builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres", HealthStatus.Unhealthy)
    .AddCheck<GarageHealthCheck>("garage", HealthStatus.Degraded);

var app = builder.Build();

app.UseOpenTelemetry();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }).DisableHttpMetrics();
app.MapHealthChecks("/health/ready").DisableHttpMetrics();

await app.RunAsync();
