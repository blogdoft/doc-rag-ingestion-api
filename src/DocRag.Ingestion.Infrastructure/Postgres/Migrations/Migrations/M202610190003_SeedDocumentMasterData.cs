using FluentMigrator;

namespace DocRag.Ingestion.Infrastructure.Postgres.Migrations.Migrations;

/// <summary>
/// Registers every type and language already used by <c>documents</c> in the master data. PostgreSQL 16 has no
/// <c>uuidv7()</c>, so the UUID v7 is composed here: a 48-bit millisecond timestamp followed by the random bits
/// of <c>gen_random_uuid()</c> with the version nibble switched from 4 to 7.
/// </summary>
[Migration(202610190003, "Seed document types and languages from documents")]
public sealed class SeedDocumentMasterData : ForwardOnlyMigration
{
    public override void Up() => Execute.Sql("""
        INSERT INTO document_types (public_id, name)
        SELECT encode(
                   set_bit(
                       set_bit(
                           overlay(uuid_send(gen_random_uuid())
                                   placing substring(int8send(floor(extract(epoch FROM clock_timestamp()) * 1000)::bigint) FROM 3)
                                   FROM 1 FOR 6),
                           52, 1),
                       53, 1),
                   'hex')::uuid
             , source.name
        FROM (
            SELECT DISTINCT ON (lower(btrim(type))) btrim(type) AS name
            FROM documents
            WHERE btrim(type) <> ''
            ORDER BY lower(btrim(type)), created_at, id
        ) AS source
        ON CONFLICT DO NOTHING;

        INSERT INTO document_languages (public_id, code)
        SELECT encode(
                   set_bit(
                       set_bit(
                           overlay(uuid_send(gen_random_uuid())
                                   placing substring(int8send(floor(extract(epoch FROM clock_timestamp()) * 1000)::bigint) FROM 3)
                                   FROM 1 FOR 6),
                           52, 1),
                       53, 1),
                   'hex')::uuid
             , source.code
        FROM (
            SELECT DISTINCT ON (lower(btrim(language))) btrim(language) AS code
            FROM documents
            WHERE btrim(language) <> ''
            ORDER BY lower(btrim(language)), created_at, id
        ) AS source
        ON CONFLICT DO NOTHING;
        """);
}
