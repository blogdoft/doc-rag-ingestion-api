using FluentMigrator;

namespace DocRag.Ingestion.Infrastructure.Postgres.Migrations.Migrations;

[Migration(202610150004, "Create outbox messages and idempotency records")]
public sealed class CreateOutboxAndIdempotency : ForwardOnlyMigration
{
    public override void Up() => Execute.Sql("""
        CREATE TABLE outbox_messages (
            id              bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            event_id        uuid        NOT NULL,
            event_type      text        NOT NULL,
            aggregate_id    uuid        NOT NULL,
            tenant_id       text        NOT NULL,
            payload         jsonb       NOT NULL,
            occurred_at     timestamptz NOT NULL,
            published_at    timestamptz NULL,
            attempts        int         NOT NULL DEFAULT 0,
            next_attempt_at timestamptz NOT NULL DEFAULT now(),
            lease_until     timestamptz NULL,
            last_error      text        NULL,
            CONSTRAINT ux_outbox_messages_event_id UNIQUE (event_id)
        );

        CREATE INDEX ix_outbox_messages_pending ON outbox_messages (next_attempt_at, id) WHERE published_at IS NULL;

        CREATE TABLE idempotency_records (
            tenant_id           text         NOT NULL,
            operation           text         NOT NULL,
            idempotency_key     varchar(128) NOT NULL,
            request_fingerprint text         NULL,
            response_status     int          NULL,
            resource_id         uuid         NULL,
            state               varchar(20)  NOT NULL,
            expires_at          timestamptz  NOT NULL,
            created_at          timestamptz  NOT NULL DEFAULT now(),
            CONSTRAINT pk_idempotency_records PRIMARY KEY (tenant_id, operation, idempotency_key),
            CONSTRAINT ck_idempotency_records_state CHECK (state IN ('in_progress', 'completed'))
        );

        CREATE INDEX ix_idempotency_records_expires_at ON idempotency_records (expires_at);
        """);
}
