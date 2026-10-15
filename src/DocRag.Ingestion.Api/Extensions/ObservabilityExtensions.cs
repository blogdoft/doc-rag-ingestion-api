using BlogDoFT.Libs.Api.OpenTelemetry.Extensions;
using DocRag.Ingestion.Application.Common;
using Npgsql;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace DocRag.Ingestion.Api.Extensions;

/// <summary>Logging, metrics and tracing through <c>BlogDoFT.Libs.Api.OpenTelemetry</c>.</summary>
public static class ObservabilityExtensions
{
    /// <summary>Kubernetes probe path prefix, kept out of traces and HTTP metrics.</summary>
    public const string HealthPath = "/health";

    /// <summary>
    /// Structured JSON logs on stdout (with scopes, so the correlation id is on every line) and
    /// OpenTelemetry metrics/traces driven by the <c>Observability</c> configuration section.
    /// Adds the ingestion meter and activity source and PostgreSQL spans.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <param name="defaultServiceName">Service name used when <c>OTEL_SERVICE_NAME</c> is unset.</param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder, string defaultServiceName)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
        });

        builder.Services.AddOtel(builder.Configuration);
        builder.Services.Configure<AspNetCoreTraceInstrumentationOptions>(options =>
            options.Filter = httpContext => !httpContext.Request.Path.StartsWithSegments(HealthPath));

        var serviceName = builder.Configuration["OTEL_SERVICE_NAME"] ?? defaultServiceName;
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithMetrics(metrics => metrics.AddMeter(IngestionTelemetry.Name))
            .WithTracing(tracing => tracing.AddSource(IngestionTelemetry.Name).AddNpgsql());

        return builder;
    }

    /// <summary>Maps the metrics scraping endpoint when Prometheus is the configured exporter.</summary>
    /// <param name="app">The web application.</param>
    /// <returns>The same <paramref name="app"/>, for chaining.</returns>
    public static WebApplication UseObservability(this WebApplication app)
    {
        app.UseOpenTelemetry();
        return app;
    }
}
