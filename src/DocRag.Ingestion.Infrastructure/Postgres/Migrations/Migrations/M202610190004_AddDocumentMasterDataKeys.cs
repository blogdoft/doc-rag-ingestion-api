using FluentMigrator;

namespace DocRag.Ingestion.Infrastructure.Postgres.Migrations.Migrations;

/// <summary>Adds the foreign keys as nullable columns; the next migration fills them before they become mandatory.</summary>
[Migration(202610190004, "Add master data foreign keys to documents")]
public sealed class AddDocumentMasterDataKeys : ForwardOnlyMigration
{
    public override void Up() => Execute.Sql("""
        ALTER TABLE documents
            ADD COLUMN document_type_id bigint NULL,
            ADD COLUMN document_language_id bigint NULL;

        ALTER TABLE documents ADD CONSTRAINT fk_documents_document_type
            FOREIGN KEY (document_type_id) REFERENCES document_types (id);
        ALTER TABLE documents ADD CONSTRAINT fk_documents_document_language
            FOREIGN KEY (document_language_id) REFERENCES document_languages (id);

        CREATE INDEX ix_documents_document_type_id ON documents (document_type_id);
        CREATE INDEX ix_documents_document_language_id ON documents (document_language_id);
        """);
}
