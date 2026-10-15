using FluentMigrator;

namespace DocRag.Ingestion.Infrastructure.Postgres.Migrations.Migrations;

[Migration(202610150002, "Create document versions")]
public sealed class CreateDocumentVersions : ForwardOnlyMigration
{
    public override void Up() => Execute.Sql("""
        CREATE TABLE document_versions (
            id               bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            public_id        uuid         NOT NULL,
            tenant_id        text         NOT NULL,
            document_id      bigint       NOT NULL,
            label            varchar(100) NOT NULL,
            published_at     timestamptz  NULL,
            effective_from   timestamptz  NULL,
            effective_to     timestamptz  NULL,
            ingestion_status varchar(30)  NOT NULL DEFAULT 'awaiting_upload',
            created_at       timestamptz  NOT NULL DEFAULT now(),
            updated_at       timestamptz  NOT NULL DEFAULT now(),
            CONSTRAINT ux_document_versions_public_id UNIQUE (public_id),
            CONSTRAINT ux_document_versions_id_tenant UNIQUE (id, tenant_id),
            CONSTRAINT ux_document_versions_label UNIQUE (tenant_id, document_id, label),
            CONSTRAINT fk_document_versions_document FOREIGN KEY (document_id, tenant_id)
                REFERENCES documents (id, tenant_id),
            CONSTRAINT ck_document_versions_status CHECK (ingestion_status IN ('awaiting_upload', 'uploading', 'uploaded', 'upload_failed')),
            CONSTRAINT ck_document_versions_effective CHECK (effective_to IS NULL OR effective_from IS NULL OR effective_to >= effective_from)
        );

        CREATE INDEX ix_document_versions_listing ON document_versions (tenant_id, document_id, public_id DESC);
        CREATE INDEX ix_document_versions_uploading ON document_versions (ingestion_status) WHERE ingestion_status = 'uploading';
        """);
}
