using FluentMigrator;

namespace DocRag.Ingestion.Infrastructure.Postgres.Migrations.Migrations;

[Migration(202610150001, "Create documents")]
public sealed class CreateDocuments : ForwardOnlyMigration
{
    public override void Up() => Execute.Sql("""
        CREATE TABLE documents (
            id          bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            public_id   uuid         NOT NULL,
            tenant_id   text         NOT NULL,
            title       varchar(300) NOT NULL,
            description text         NULL,
            type        varchar(100) NOT NULL,
            language    varchar(35)  NOT NULL,
            tags        jsonb        NOT NULL DEFAULT '[]'::jsonb,
            metadata    jsonb        NOT NULL DEFAULT '{}'::jsonb,
            revision    bigint       NOT NULL DEFAULT 1,
            created_at  timestamptz  NOT NULL DEFAULT now(),
            updated_at  timestamptz  NOT NULL DEFAULT now(),
            CONSTRAINT ux_documents_public_id UNIQUE (public_id),
            CONSTRAINT ux_documents_id_tenant UNIQUE (id, tenant_id),
            CONSTRAINT ck_documents_tenant_not_empty CHECK (length(tenant_id) > 0),
            CONSTRAINT ck_documents_metadata_object CHECK (jsonb_typeof(metadata) = 'object'),
            CONSTRAINT ck_documents_tags_array CHECK (jsonb_typeof(tags) = 'array')
        );

        CREATE INDEX ix_documents_tenant_public_id ON documents (tenant_id, public_id);
        CREATE INDEX ix_documents_tenant_type ON documents (tenant_id, type);
        """);
}
