using DocRag.Ingestion.Application.Abstractions;
using DocRag.Ingestion.Infrastructure.Postgres.Migrations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace DocRag.Ingestion.Api.Tests.Support;

/// <summary>Boots the real HTTP pipeline with every port replaced by a substitute (no database or storage).</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string SigningKey = "test-signing-key-0123456789-0123456789";
    public const string Issuer = "doc-rag-tests";
    public const string Audience = "doc-rag-ingestion";

    public IDocumentRepository Documents { get; } = Substitute.For<IDocumentRepository>();

    public IDocumentVersionRepository Versions { get; } = Substitute.For<IDocumentVersionRepository>();

    public IFileRepository Files { get; } = Substitute.For<IFileRepository>();

    public IUploadAttemptRepository Attempts { get; } = Substitute.For<IUploadAttemptRepository>();

    public IOutboxRepository Outbox { get; } = Substitute.For<IOutboxRepository>();

    public IIdempotencyStore Idempotency { get; } = Substitute.For<IIdempotencyStore>();

    public IObjectStorage Storage { get; } = Substitute.For<IObjectStorage>();

    public IUnitOfWork UnitOfWork { get; } = Substitute.For<IUnitOfWork>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=unused;Username=unused;Password=unused",
            ["Auth:SigningKey"] = SigningKey,
            ["Auth:Issuer"] = Issuer,
            ["Auth:Audience"] = Audience,
            ["Auth:RequireHttpsMetadata"] = "false",
            ["Garage:ServiceUrl"] = "http://localhost:1",
            ["Garage:AccessKey"] = "unused",
            ["Garage:SecretKey"] = "unused",
            ["Upload:MaxBytes"] = "1024",
        }));

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DatabaseMigrator>();
            Replace(services, Documents);
            Replace(services, Versions);
            Replace(services, Files);
            Replace(services, Attempts);
            Replace(services, Outbox);
            Replace(services, Idempotency);
            Replace(services, Storage);
            Replace(services, UnitOfWork);
        });
    }

    private static void Replace<T>(IServiceCollection services, T instance)
        where T : class
    {
        services.RemoveAll<T>();
        services.AddSingleton(instance);
    }
}
