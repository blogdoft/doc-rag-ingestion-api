using FluentMigrator;

namespace DocRag.Ingestion.Infrastructure.Postgres.Migrations.Migrations;

[Migration(202610190002, "Create document languages")]
public sealed class CreateDocumentLanguages : ForwardOnlyMigration
{
    public override void Up() => Execute.Sql("""
        CREATE TABLE document_languages (
            id         bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            public_id  uuid        NOT NULL,
            code       varchar(35) NOT NULL,
            created_at timestamptz NOT NULL DEFAULT now(),
            updated_at timestamptz NOT NULL DEFAULT now(),
            CONSTRAINT ux_document_languages_public_id UNIQUE (public_id),
            CONSTRAINT ck_document_languages_code_not_empty CHECK (length(btrim(code)) > 0)
        );

        CREATE UNIQUE INDEX ux_document_languages_code ON document_languages (lower(code));
        """);
}
