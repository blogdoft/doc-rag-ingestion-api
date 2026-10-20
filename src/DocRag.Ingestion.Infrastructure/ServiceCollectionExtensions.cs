using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Application.MasterData;
using DocRag.Ingestion.Infrastructure.Garage;
using DocRag.Ingestion.Infrastructure.Kafka;
using DocRag.Ingestion.Infrastructure.Postgres;
using DocRag.Ingestion.Infrastructure.Postgres.Migrations;
using DocRag.Ingestion.Infrastructure.Postgres.Migrations.Migrations;
using DocRag.Ingestion.Infrastructure.Postgres.Repositories;
using FluentMigrator.Runner;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DocRag.Ingestion.Infrastructure;

/// <summary>Dependency injection of the infrastructure adapters.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers the Npgsql data source, the unit of work and the Dapper repositories.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddPostgresPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        // Resolved on first use, not at registration time, so configuration sources added later
        // (such as a test host's overrides) are still honoured.
        services.AddSingleton(sp => new NpgsqlDataSourceBuilder(ConnectionString(sp)).Build());
        services.AddScoped<DbSession>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<DbSession>());
        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddScoped<IDocumentVersionRepository, DocumentVersionRepository>();
        services.AddScoped<IDocumentTypeRepository, DocumentTypeRepository>();
        services.AddScoped<IDocumentLanguageRepository, DocumentLanguageRepository>();
        services.AddScoped<IFileRepository, FileRepository>();
        services.AddScoped<IUploadAttemptRepository, UploadAttemptRepository>();
        services.AddScoped<IOutboxRepository, OutboxRepository>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        return services;
    }

    /// <summary>Registers the schema migrator as a warm-up command (call <c>AddWarmUp()</c> after it).</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddDatabaseMigrations(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(sp => new PostgresDatabaseOptions(ConnectionString(sp)));
        services.AddFluentMigratorCore()
            .ConfigureRunner(runner => runner
                .AddPostgres()
                .WithGlobalConnectionString(ConnectionString)
                .ScanIn(typeof(CreateDocuments).Assembly).For.Migrations().For.VersionTableMetaData());
        services.AddSingleton<DatabaseMigrator>();
        return services;
    }

    /// <summary>Registers the Garage object storage with startup-validated options.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddGarageStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GarageOptions>().Bind(configuration.GetSection(GarageOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<IObjectStorage, GarageObjectStorage>();
        return services;
    }

    /// <summary>Registers the Kafka publisher with startup-validated options.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same <paramref name="services"/>, for chaining.</returns>
    public static IServiceCollection AddKafkaPublisher(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<KafkaOptions>().Bind(configuration.GetSection(KafkaOptions.SectionName)).ValidateDataAnnotations().ValidateOnStart();
        services.AddSingleton<IEventPublisher, KafkaEventPublisher>();
        return services;
    }

    private static string ConnectionString(IServiceProvider services) =>
        services.GetRequiredService<IConfiguration>().GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("Missing required 'ConnectionStrings:Postgres' configuration value.");
}
