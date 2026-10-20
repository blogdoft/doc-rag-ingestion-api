using FluentMigrator;

namespace DocRag.Ingestion.Infrastructure.Postgres.Migrations.Migrations;

/// <summary>
/// Makes the keys mandatory and drops the descriptive columns they replace. <c>SET NOT NULL</c> runs first, so a
/// document the backfill could not match aborts the migration (it is transactional) instead of losing its text.
/// </summary>
[Migration(202610190006, "Drop type and language text columns of documents")]
public sealed class DropDocumentTypeAndLanguageText : ForwardOnlyMigration
{
    public override void Up() => Execute.Sql("""
        ALTER TABLE documents
            ALTER COLUMN document_type_id SET NOT NULL,
            ALTER COLUMN document_language_id SET NOT NULL;

        ALTER TABLE documents
            DROP COLUMN type,
            DROP COLUMN language;
        """);
}
