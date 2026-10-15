# Database rollback

Migrations are **forward-only** (FluentMigrator `ForwardOnlyMigration`, applied under a PostgreSQL
advisory lock by the API's warm-up task). Production rollbacks are therefore *new forward migrations* or,
for an emergency, the manual scripts below executed by a DBA **after stopping the API and the worker**.
Take a backup first. Dropping a table destroys its data; the original files in Garage are not touched.

Order matters because of foreign keys: undo the newest migration first, then remove its row from
`doc_rag_ingestion_schema_versions` so the migration can be re-applied later.

## 202610150005 - tenant removal

This migration drops every `tenant_id` column and cannot be undone: the dropped values are not recoverable.
To go back to the tenant-scoped schema, restore a backup taken before it ran, or re-add the columns with a new
forward migration and backfill them yourself. The previous migrations keep creating the tenant columns, so a fresh
database ends up identical either way.

## 202610150004 - outbox and idempotency

```sql
DROP TABLE IF EXISTS idempotency_records;
DROP TABLE IF EXISTS outbox_messages;
DELETE FROM doc_rag_ingestion_schema_versions WHERE version = 202610150004;
```

Pending (unpublished) outbox rows are lost: export them first if events must still be delivered.

## 202610150003 - files and upload attempts

```sql
DROP TABLE IF EXISTS upload_attempts;
DROP TABLE IF EXISTS document_files;
DELETE FROM doc_rag_ingestion_schema_versions WHERE version = 202610150003;
```

Objects in the bucket become unreferenced; list the bucket and delete them or restore the tables.

## 202610150002 - document versions

```sql
DROP TABLE IF EXISTS document_versions;
DELETE FROM doc_rag_ingestion_schema_versions WHERE version = 202610150002;
```

## 202610150001 - documents

```sql
DROP TABLE IF EXISTS documents;
DELETE FROM doc_rag_ingestion_schema_versions WHERE version = 202610150001;
```

## Safer alternative

Prefer rolling forward: ship a corrective migration (new version number) instead of dropping data, and
roll back the *application* image only (the previous image must tolerate the newer schema - additive
changes only).
