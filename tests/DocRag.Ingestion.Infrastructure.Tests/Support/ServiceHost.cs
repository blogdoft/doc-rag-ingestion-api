using DocRag.Ingestion.Application;
using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DocRag.Ingestion.Infrastructure.Tests.Support;

/// <summary>A DI container with the real infrastructure adapters, for tests below the HTTP layer.</summary>
public static class ServiceHost
{
    public static ServiceProvider Build(
        string connectionString,
        string garageUrl,
        string bucket,
        string? kafkaBootstrap = null,
        Action<IServiceCollection>? configure = null,
        IDictionary<string, string?>? settings = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = connectionString,
            ["Garage:ServiceUrl"] = garageUrl,
            ["Garage:Bucket"] = bucket,
            ["Garage:AccessKey"] = InfrastructureFixture.GarageAccessKey,
            ["Garage:SecretKey"] = InfrastructureFixture.GarageSecretKey,
            ["Garage:PartSizeMiB"] = "5",
            ["Kafka:BootstrapServers"] = kafkaBootstrap ?? "localhost:1",
            ["Kafka:TopicDocumentUploaded"] = "doc-rag.documents.uploaded.v1",
        };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            values[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(logging => logging.AddProvider(new CapturingLoggerProvider()));
        services.AddApplicationServices(configuration);
        services.AddBackgroundProcessing(configuration);
        services.AddPostgresPersistence(configuration);
        services.AddGarageStorage(configuration);
        services.AddDatabaseMigrations(configuration);
        if (kafkaBootstrap is not null)
        {
            services.AddKafkaPublisher(configuration);
        }

        configure?.Invoke(services);
        return services.BuildServiceProvider(validateScopes: true);
    }

    public static async Task MigrateAsync(IServiceProvider provider) =>
        await provider.GetRequiredService<DocRag.Ingestion.Infrastructure.Postgres.Migrations.DatabaseMigrator>().Execute();
}
