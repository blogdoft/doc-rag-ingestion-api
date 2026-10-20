using FluentMigrator;

namespace DocRag.Ingestion.Infrastructure.Postgres.Migrations.Migrations;

/// <summary>Points each document at the master data row whose descriptive value equals the document's text, ignoring case and padding.</summary>
[Migration(202610190005, "Backfill master data foreign keys of documents")]
public sealed class BackfillDocumentMasterDataKeys : ForwardOnlyMigration
{
    public override void Up() => Execute.Sql("""
        UPDATE documents AS d
        SET document_type_id = t.id
        FROM document_types AS t
        WHERE d.document_type_id IS NULL
          AND lower(t.name) = lower(btrim(d.type));

        UPDATE documents AS d
        SET document_language_id = l.id
        FROM document_languages AS l
        WHERE d.document_language_id IS NULL
          AND lower(l.code) = lower(btrim(d.language));
        """);
}
