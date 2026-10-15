using DocRag.Ingestion.Infrastructure.Health;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DocRag.Ingestion.Api.Extensions;

/// <summary>Liveness and readiness probes.</summary>
public static class HealthProbeExtensions
{
    /// <summary>Registers PostgreSQL and Garage readiness checks (the warm-up check is added by <c>AddWarmUp</c>).</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddReadinessChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck<PostgresHealthCheck>("postgres", HealthStatus.Unhealthy)
            .AddCheck<GarageHealthCheck>("garage", HealthStatus.Unhealthy);
        return services;
    }

    /// <summary>
    /// Maps <c>/health/live</c> (process is up, no dependency checked) and <c>/health/ready</c>
    /// (migrations applied, PostgreSQL and the Garage bucket reachable). Anonymous and excluded from
    /// OpenAPI and HTTP metrics.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The same <paramref name="app"/>, for chaining.</returns>
    public static WebApplication MapHealthProbes(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
            .ExcludeFromDescription().AllowAnonymous().DisableHttpMetrics();
        app.MapHealthChecks("/health/ready").ExcludeFromDescription().AllowAnonymous().DisableHttpMetrics();
        return app;
    }
}
