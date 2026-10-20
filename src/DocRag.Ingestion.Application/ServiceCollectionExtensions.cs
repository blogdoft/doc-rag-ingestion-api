using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Application.Common;
using DocRag.Ingestion.Application.Documents;
using DocRag.Ingestion.Application.Files;
using DocRag.Ingestion.Application.Idempotency;
using DocRag.Ingestion.Application.MasterData;
using DocRag.Ingestion.Application.Options;
using DocRag.Ingestion.Application.Outbox;
using DocRag.Ingestion.Application.Versions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DocRag.Ingestion.Application;

/// <summary>Dependency injection of the application layer.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers the use cases, validated options and the system clock.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<UploadOptions>().Bind(configuration.GetSection(UploadOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IdempotencyGate>();
        services.AddScoped<DocumentService>();
        services.AddScoped<DocumentTypeService>();
        services.AddScoped<DocumentLanguageService>();
        services.AddScoped<VersionService>();
        services.AddScoped<FileUploadService>();
        return services;
    }

    /// <summary>Registers the outbox dispatcher and the reconciler, which only the worker runs.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddBackgroundProcessing(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OutboxOptions>().Bind(configuration.GetSection(OutboxOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<ReconciliationOptions>().Bind(configuration.GetSection(ReconciliationOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddScoped<UploadReconciler>();
        services.AddScoped<OutboxDispatcher>();
        return services;
    }
}
