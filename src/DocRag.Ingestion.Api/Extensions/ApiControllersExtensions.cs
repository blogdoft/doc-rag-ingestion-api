using System.Text.Json;
using System.Text.Json.Serialization;
using DocRag.Ingestion.Api.Filters;
using Microsoft.AspNetCore.Mvc;

namespace DocRag.Ingestion.Api.Extensions;

/// <summary>MVC registration with the API's cross-cutting behaviour.</summary>
public static class ApiControllersExtensions
{
    /// <summary>
    /// Registers controllers with <c>System.Text.Json</c> in camelCase, strict (unknown members are
    /// rejected), the global exception filter and the single validation-to-<c>ProblemDetails</c> filter.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddApiControllers(this IServiceCollection services)
    {
        services
            .AddControllers(options =>
            {
                options.Filters.Add<UnhandledExceptionFilter>();
                options.Filters.Add<ModelValidationFilter>();
                options.Filters.Add<MvcRejectionProblemFilter>();
            })
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
                options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            });

        services.Configure<ApiBehaviorOptions>(options =>
        {
            // ModelValidationFilter is the single place that turns binding failures into a 400 problem.
            options.SuppressModelStateInvalidFilter = true;

            // Without this, [ApiController] rewrites bare 404/401/403 results into problem bodies,
            // but those statuses must carry no body.
            options.SuppressMapClientErrors = true;
        });

        return services;
    }
}
