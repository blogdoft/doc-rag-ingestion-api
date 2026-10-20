using FluentMigrator;

namespace DocRag.Ingestion.Infrastructure.Postgres.Migrations.Migrations;

[Migration(202610190001, "Create document types")]
public sealed class CreateDocumentTypes : ForwardOnlyMigration
{
    public override void Up() => Execute.Sql("""
        CREATE TABLE document_types (
            id         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            public_id  uuid         NOT NULL,
            name       varchar(100) NOT NULL,
            created_at timestamptz  NOT NULL DEFAULT now(),
            updated_at timestamptz  NOT NULL DEFAULT now(),
            CONSTRAINT ux_document_types_public_id UNIQUE (public_id),
            CONSTRAINT ck_document_types_name_not_empty CHECK (length(btrim(name)) > 0)
        );

        CREATE UNIQUE INDEX ux_document_types_name ON document_types (lower(name));
        """);
}
