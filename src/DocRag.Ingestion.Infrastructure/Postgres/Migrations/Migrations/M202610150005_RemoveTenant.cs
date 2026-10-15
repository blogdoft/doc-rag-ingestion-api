using FluentMigrator;

namespace DocRag.Ingestion.Infrastructure.Postgres.Migrations.Migrations;

[Migration(202610150005, "Remove tenant isolation")]
public sealed class RemoveTenant : ForwardOnlyMigration
{
    public override void Up() => Execute.Sql("""
        ALTER TABLE document_files DROP CONSTRAINT fk_document_files_version;
        ALTER TABLE upload_attempts DROP CONSTRAINT fk_upload_attempts_version;
        ALTER TABLE document_versions DROP CONSTRAINT fk_document_versions_document;

        ALTER TABLE document_versions DROP CONSTRAINT ux_document_versions_label;
        ALTER TABLE document_versions DROP CONSTRAINT ux_document_versions_id_tenant;
        ALTER TABLE documents DROP CONSTRAINT ux_documents_id_tenant;

        -- Dropping a column also drops the indexes and CHECK constraints that mention it.
        ALTER TABLE documents DROP COLUMN tenant_id;
        ALTER TABLE document_versions DROP COLUMN tenant_id;
        ALTER TABLE document_files DROP COLUMN tenant_id;
        ALTER TABLE upload_attempts DROP COLUMN tenant_id;
        ALTER TABLE outbox_messages DROP COLUMN tenant_id;

        ALTER TABLE document_versions ADD CONSTRAINT ux_document_versions_label UNIQUE (document_id, label);
        ALTER TABLE document_versions ADD CONSTRAINT fk_document_versions_document
            FOREIGN KEY (document_id) REFERENCES documents (id);
        ALTER TABLE document_files ADD CONSTRAINT fk_document_files_version
            FOREIGN KEY (document_version_id) REFERENCES document_versions (id);
        ALTER TABLE upload_attempts ADD CONSTRAINT fk_upload_attempts_version
            FOREIGN KEY (document_version_id) REFERENCES document_versions (id);

        CREATE INDEX ix_document_versions_listing ON document_versions (document_id, public_id DESC);

        -- The same (operation, key) may exist for several tenants; keep only the newest before the key narrows.
        DELETE FROM idempotency_records AS older
        USING idempotency_records AS newer
        WHERE older.operation = newer.operation
          AND older.idempotency_key = newer.idempotency_key
          AND (older.created_at, older.ctid) < (newer.created_at, newer.ctid);
        ALTER TABLE idempotency_records DROP CONSTRAINT pk_idempotency_records;
        ALTER TABLE idempotency_records DROP COLUMN tenant_id;
        ALTER TABLE idempotency_records ADD CONSTRAINT pk_idempotency_records PRIMARY KEY (operation, idempotency_key);
        """);
}
