using FluentMigrator;

namespace DocRag.Ingestion.Infrastructure.Postgres.Migrations.Migrations;

[Migration(202610150003, "Create document files and upload attempts")]
public sealed class CreateFilesAndUploadAttempts : ForwardOnlyMigration
{
    public override void Up() => Execute.Sql("""
        CREATE TABLE document_files (
            id                  bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            public_id           uuid        NOT NULL,
            tenant_id           text        NOT NULL,
            document_version_id bigint      NOT NULL,
            original_name       text        NOT NULL,
            content_type        text        NOT NULL,
            size_bytes          bigint      NOT NULL,
            sha256              char(64)    NOT NULL,
            bucket              text        NOT NULL,
            object_key          text        NOT NULL,
            storage_state       text        NOT NULL DEFAULT 'stored',
            uploaded_at         timestamptz NOT NULL,
            created_at          timestamptz NOT NULL DEFAULT now(),
            CONSTRAINT ux_document_files_public_id UNIQUE (public_id),
            CONSTRAINT fk_document_files_version FOREIGN KEY (document_version_id, tenant_id)
                REFERENCES document_versions (id, tenant_id),
            CONSTRAINT ck_document_files_size CHECK (size_bytes > 0),
            CONSTRAINT ck_document_files_sha256 CHECK (sha256 ~ '^[a-f0-9]{64}$')
        );

        -- One completed original per version: a second INSERT fails even under concurrency.
        CREATE UNIQUE INDEX ux_document_files_one_original_per_version
            ON document_files (document_version_id) WHERE storage_state = 'stored';

        CREATE TABLE upload_attempts (
            id                   bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            public_id            uuid        NOT NULL,
            tenant_id            text        NOT NULL,
            document_version_id  bigint      NOT NULL,
            file_id              uuid        NOT NULL,
            status               varchar(20) NOT NULL,
            temporary_object_key text        NOT NULL,
            lease_expires_at     timestamptz NOT NULL,
            bytes_received       bigint      NOT NULL DEFAULT 0,
            failure_code         text        NULL,
            object_cleaned       boolean     NOT NULL DEFAULT FALSE,
            created_at           timestamptz NOT NULL DEFAULT now(),
            updated_at           timestamptz NOT NULL DEFAULT now(),
            CONSTRAINT ux_upload_attempts_public_id UNIQUE (public_id),
            CONSTRAINT fk_upload_attempts_version FOREIGN KEY (document_version_id, tenant_id)
                REFERENCES document_versions (id, tenant_id),
            CONSTRAINT ck_upload_attempts_status CHECK (status IN ('active', 'completed', 'failed', 'expired'))
        );

        -- At most one active upload per version.
        CREATE UNIQUE INDEX ux_upload_attempts_one_active_per_version
            ON upload_attempts (document_version_id) WHERE status = 'active';
        CREATE INDEX ix_upload_attempts_tenant_active ON upload_attempts (tenant_id) WHERE status = 'active';
        CREATE INDEX ix_upload_attempts_lease ON upload_attempts (lease_expires_at) WHERE status = 'active';
        CREATE INDEX ix_upload_attempts_cleanup ON upload_attempts (id)
            WHERE status IN ('failed', 'expired') AND NOT object_cleaned;
        """);
}
