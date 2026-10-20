using DocRag.Ingestion.Infrastructure.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;

namespace DocRag.Ingestion.Infrastructure.Tests.Postgres;

[Collection(InfrastructureCollection.Name)]
public class SchemaAndMigrationTests(InfrastructureFixture infrastructure) : IAsyncLifetime
{
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        _connectionString = await infrastructure.NewDatabaseAsync();
        await using var provider = ServiceHost.Build(_connectionString, infrastructure.GarageServiceUrl, "unused");
        await ServiceHost.MigrateAsync(provider);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static async Task<long> InsertAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<long> InsertDocumentAsync(NpgsqlConnection connection)
    {
        var typeId = await InsertAsync(connection, $"INSERT INTO document_types (public_id, name) VALUES ('{NewUuid()}', 't') ON CONFLICT (lower(name)) DO UPDATE SET name = EXCLUDED.name RETURNING id");
        var languageId = await InsertAsync(connection, $"INSERT INTO document_languages (public_id, code) VALUES ('{NewUuid()}', 'pt-BR') ON CONFLICT (lower(code)) DO UPDATE SET code = EXCLUDED.code RETURNING id");
        return await InsertAsync(connection, $"INSERT INTO documents (public_id, title, document_type_id, document_language_id) VALUES ('{NewUuid()}', 't', {typeId}, {languageId}) RETURNING id");
    }

    private static string NewUuid() => Guid.CreateVersion7().ToString();

    [Fact]
    public async Task Should_ApplyMigrationsSafely_When_ManyReplicasStartAtTheSameTime()
    {
        var database = await infrastructure.NewDatabaseAsync();
        var providers = Enumerable.Range(0, 4).Select(_ => ServiceHost.Build(database, infrastructure.GarageServiceUrl, "unused")).ToList();

        await Task.WhenAll(providers.Select(ServiceHost.MigrateAsync));

        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        (await InsertAsync(connection, "SELECT count(*) FROM doc_rag_ingestion_schema_versions")).ShouldBeGreaterThanOrEqualTo(4);
        providers.ForEach(p => p.Dispose());
    }

    [Fact]
    public async Task Should_BeIdempotent_When_MigrationsRunTwice()
    {
        await using var provider = ServiceHost.Build(_connectionString, infrastructure.GarageServiceUrl, "unused");

        var again = async () => await ServiceHost.MigrateAsync(provider);

        await Should.NotThrowAsync(again);
    }

    [Fact]
    public async Task Should_RejectAVersionOfAMissingDocument_When_TheForeignKeyIsEnforced()
    {
        await using var connection = await OpenAsync();

        var insert = async () => await InsertAsync(connection,
            $"INSERT INTO document_versions (public_id, document_id, label) VALUES ('{NewUuid()}', 999999, '1') RETURNING id");

        var exception = await insert.ShouldThrowAsync<PostgresException>();
        exception.SqlState.ShouldBe(PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task Should_HaveNoTenantColumns_When_AllMigrationsRan()
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM information_schema.columns WHERE table_schema = 'public' AND column_name = 'tenant_id'",
            connection);

        (await command.ExecuteScalarAsync()).ShouldBe(0L);
    }

    [Fact]
    public async Task Should_RejectDuplicateLabelsInTheSameDocument_When_TheDocumentMatches()
    {
        await using var connection = await OpenAsync();
        var documentId = await InsertDocumentAsync(connection);
        await InsertAsync(connection, $"INSERT INTO document_versions (public_id, document_id, label) VALUES ('{NewUuid()}', {documentId}, '4.00') RETURNING id");

        var duplicate = async () => await InsertAsync(connection,
            $"INSERT INTO document_versions (public_id, document_id, label) VALUES ('{NewUuid()}', {documentId}, '4.00') RETURNING id");

        (await duplicate.ShouldThrowAsync<PostgresException>()).ConstraintName.ShouldBe("ux_document_versions_label");
    }

    [Fact]
    public async Task Should_RejectInvertedEffectiveRange_When_ConstraintIsEnforcedByTheDatabase()
    {
        await using var connection = await OpenAsync();
        var documentId = await InsertDocumentAsync(connection);

        var insert = async () => await InsertAsync(connection,
            $"INSERT INTO document_versions (public_id, document_id, label, effective_from, effective_to) VALUES ('{NewUuid()}', {documentId}, '1', now(), now() - interval '1 day') RETURNING id");

        (await insert.ShouldThrowAsync<PostgresException>()).SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task Should_AllowOnlyOneStoredFileAndOneActiveAttemptPerVersion_When_ConstraintsAreEnforced()
    {
        await using var connection = await OpenAsync();
        var documentId = await InsertDocumentAsync(connection);
        var versionId = await InsertAsync(connection, $"INSERT INTO document_versions (public_id, document_id, label) VALUES ('{NewUuid()}', {documentId}, '1') RETURNING id");
        string File() => $"INSERT INTO document_files (public_id, document_version_id, original_name, content_type, size_bytes, sha256, bucket, object_key, uploaded_at) VALUES ('{NewUuid()}', {versionId}, 'f', 'application/pdf', 1, '{new string('a', 64)}', 'b', 'k{NewUuid()}', now()) RETURNING id";
        string Attempt() => $"INSERT INTO upload_attempts (public_id, document_version_id, file_id, status, temporary_object_key, lease_expires_at) VALUES ('{NewUuid()}', {versionId}, '{NewUuid()}', 'active', 'k', now()) RETURNING id";
        await InsertAsync(connection, File());
        await InsertAsync(connection, Attempt());

        var secondFile = async () => await InsertAsync(connection, File());
        var secondAttempt = async () => await InsertAsync(connection, Attempt());

        (await secondFile.ShouldThrowAsync<PostgresException>()).ConstraintName.ShouldBe("ux_document_files_one_original_per_version");
        (await secondAttempt.ShouldThrowAsync<PostgresException>()).ConstraintName.ShouldBe("ux_upload_attempts_one_active_per_version");
    }

    [Fact]
    public async Task Should_UseTimestamptzColumns_When_TheSchemaIsInspected()
    {
        await using var connection = await OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*) FROM information_schema.columns
            WHERE table_schema = 'public' AND column_name LIKE '%\_at' AND data_type <> 'timestamp with time zone'
              AND table_name <> 'doc_rag_ingestion_schema_versions'
            """,
            connection);

        (await command.ExecuteScalarAsync()).ShouldBe(0L);
    }
}
