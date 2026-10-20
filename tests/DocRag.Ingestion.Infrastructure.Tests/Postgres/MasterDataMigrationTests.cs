using DocRag.Ingestion.Infrastructure.Tests.Support;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace DocRag.Ingestion.Infrastructure.Tests.Postgres;

/// <summary>Replays the move of <c>documents.type</c>/<c>language</c> to the master data over rows written by the old schema.</summary>
[Collection(InfrastructureCollection.Name)]
public class MasterDataMigrationTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private const long LastMigrationWithTextColumns = 202610190002;

    private ServiceProvider _provider = null!;
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        _connectionString = await infrastructure.NewDatabaseAsync();
        _provider = ServiceHost.Build(_connectionString, infrastructure.GarageServiceUrl, "unused");
        await using var scope = _provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp(LastMigrationWithTextColumns);
    }

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    private void MigrateToLatest()
    {
        using var scope = _provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<string>> ColumnAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<string>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private Task InsertLegacyDocumentAsync(string title, string type, string language) => ExecuteAsync(
        $"INSERT INTO documents (public_id, title, type, language) VALUES ('{Guid.CreateVersion7()}', '{title}', '{type}', '{language}')");

    [Fact]
    public async Task Should_SeedOneRecordPerDistinctValueIgnoringCaseAndPadding_When_DocumentsRepeatTypesAndLanguages()
    {
        await InsertLegacyDocumentAsync("A", "fiscal-manual", "pt-BR");
        await InsertLegacyDocumentAsync("B", "Fiscal-Manual ", "pt-br");
        await InsertLegacyDocumentAsync("C", "contract", "en");

        MigrateToLatest();

        (await ColumnAsync("SELECT name FROM document_types ORDER BY name")).ShouldBe(["contract", "fiscal-manual"]);
        (await ColumnAsync("SELECT code FROM document_languages ORDER BY code")).ShouldBe(["en", "pt-BR"]);
    }

    [Fact]
    public async Task Should_ReuseAnExistingRecord_When_TheMasterDataAlreadyHoldsTheValue()
    {
        await ExecuteAsync($"INSERT INTO document_types (public_id, name) VALUES ('{Guid.CreateVersion7()}', 'Contract')");
        await InsertLegacyDocumentAsync("C", "contract", "en");

        MigrateToLatest();

        (await ColumnAsync("SELECT name FROM document_types")).ShouldBe(["Contract"]);
        (await ColumnAsync("SELECT t.name FROM documents d JOIN document_types t ON t.id = d.document_type_id")).ShouldBe(["Contract"]);
    }

    [Fact]
    public async Task Should_PointEachDocumentAtTheRecordWithTheSameDescription_When_TheKeysAreBackfilled()
    {
        await InsertLegacyDocumentAsync("A", "fiscal-manual", "pt-BR");
        await InsertLegacyDocumentAsync("B", "Fiscal-Manual ", "pt-br");
        await InsertLegacyDocumentAsync("C", "contract", "en");

        MigrateToLatest();

        var rows = await ColumnAsync("""
            SELECT d.title || ':' || t.name || ':' || l.code
            FROM documents d
            JOIN document_types t ON t.id = d.document_type_id
            JOIN document_languages l ON l.id = d.document_language_id
            ORDER BY d.title
            """);
        rows.ShouldBe(["A:fiscal-manual:pt-BR", "B:fiscal-manual:pt-BR", "C:contract:en"]);
    }

    [Fact]
    public async Task Should_GenerateVersion7PublicIds_When_SeedingTheMasterData()
    {
        await InsertLegacyDocumentAsync("A", "fiscal-manual", "pt-BR");

        MigrateToLatest();

        var versions = await ColumnAsync("""
            SELECT substring(public_id::text FROM 15 FOR 1) FROM document_types
            UNION ALL
            SELECT substring(public_id::text FROM 15 FOR 1) FROM document_languages
            """);
        versions.ShouldBe(["7", "7"]);
    }

    [Fact]
    public async Task Should_DropTheTextColumnsAndEnforceTheKeys_When_TheMigrationsComplete()
    {
        MigrateToLatest();

        (await ColumnAsync("SELECT column_name FROM information_schema.columns WHERE table_name = 'documents' AND column_name IN ('type', 'language')"))
            .ShouldBeEmpty();
        (await ColumnAsync("SELECT is_nullable FROM information_schema.columns WHERE table_name = 'documents' AND column_name IN ('document_type_id', 'document_language_id')"))
            .ShouldBe(["NO", "NO"]);
        (await ColumnAsync("SELECT conname FROM pg_constraint WHERE conrelid = 'documents'::regclass AND contype = 'f' ORDER BY conname"))
            .ShouldBe(["fk_documents_document_language", "fk_documents_document_type"]);
    }

    [Fact]
    public async Task Should_KeepTheTextColumnsAndTheData_When_ADocumentCannotBeMatched()
    {
        await InsertLegacyDocumentAsync("A", "fiscal-manual", "pt-BR");
        await InsertLegacyDocumentAsync("blank", "   ", "pt-BR");

        Should.Throw<Exception>(MigrateToLatest);

        (await ColumnAsync("SELECT column_name FROM information_schema.columns WHERE table_name = 'documents' AND column_name IN ('type', 'language') ORDER BY column_name"))
            .ShouldBe(["language", "type"]);
        (await ColumnAsync("SELECT title FROM documents ORDER BY title")).ShouldBe(["A", "blank"]);
    }
}
