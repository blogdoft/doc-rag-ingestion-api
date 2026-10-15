---
name: csharp-infra-database
description: Coding rules for C# persistence driven-adapters built on Dapper (no EF Core) — repositories, SQL parameterization, transactions, row mapping, migrations. Use this whenever writing, reviewing, or editing a repository implementation, raw SQL, a Dapper query/execute call, a database migration script, or connection/transaction handling — even for something as small as "add a query to fetch X" or "write the upsert for Y" — as long as it touches the database access layer rather than domain or application code.
---

# C# database persistence coding standards (Dapper)

Rules for the persistence driven-adapter layer. This project family uses Dapper exclusively —
no EF Core or other ORM — so the discipline that an ORM would normally enforce (safe
parameterization, explicit mapping) has to be applied by hand, consistently.

## Repository shape

- One repository class per aggregate/entity type, implementing a port interface defined in the
  Application layer (e.g. `ICodeDocumentRepository`). The repository is the only place in the
  codebase that knows SQL exists for that aggregate.
- A repository method does data access and nothing else: no business rules, no branching on
  domain state beyond what's needed to build the query. If a decision requires domain logic, that
  logic belongs upstream, before the repository is called.
- Every method is async (`QueryAsync`, `ExecuteAsync`, `QuerySingleOrDefaultAsync`, ...) and
  accepts/forwards a `CancellationToken`.

## Use the shared Dapper abstractions

- Go through `BlogDoFT.Libs.DapperUtils.Abstractions` / `BlogDoFT.Libs.DapperUtils.Postgres`
  instead of taking a raw `IDbConnection`/`NpgsqlConnection` dependency and hand-rolling
  connection lifecycle, retry, or transaction plumbing — that's exactly the plumbing those
  packages exist to standardize. Check there before adding a new helper for something that looks
  like generic "open connection, run query" boilerplate.

## SQL safety

- Always use parameterized queries (Dapper's anonymous-object or `DynamicParameters` parameter
  binding). Never string-interpolate or concatenate a value coming from user input, a document
  field, or any variable into a SQL string — this is the #1 thing to catch in review, since it's
  the direct path to SQL injection.
- Identifiers that must vary (table/column names chosen dynamically, if ever needed) can't be
  parameterized by Dapper — if you hit this, allowlist the identifier against a known-safe set in
  code rather than passing it through unchecked.

## Mapping

- When column names and property names diverge, map explicitly (Dapper's `Query<T>(sql)` with a
  matching alias, or a splitting/multi-mapping function) rather than relying on incidental
  case-insensitive name matching to keep working across schema changes.
- Keep row-to-domain-object mapping close to the query that produces the row, so a schema change
  and its mapping update travel together in one diff.
- An `<Object-Name>Table` type represents a single table's row shape — it's the `T` in Dapper's
  `Query<T>(sql)` calls for that table, not the domain object itself.
  - These objects should have a `ToDomain()` method that maps the table object to the domain object.
  - These objects should have a `FromDomain()` method that maps a domain object to the table object
    for writes.
- When you need a projection instead of a full table row, use an anonymous type or a dedicated DTO
  type for it, mapped straight to that `Query<T>(sql)` call.
  - In this case, map the projection to the domain object inside the repository method, not in the
    application layer.

## Transactions

- Wrap multi-statement writes that must succeed or fail together in a single transaction. Keep the
  transaction's scope as short as possible — open it right before the first write, commit right
  after the last, and don't do unrelated work (network calls, embedding generation, etc.) while
  it's open.
- For this project's read → embed → upsert flow specifically: the embedding call is I/O-bound and
  slow — it must happen *before* the transaction opens, never inside it.

## Upserts

- Prefer a single `INSERT ... ON CONFLICT (...) DO UPDATE SET ...` statement over a
  select-then-insert-or-update round trip — it's both fewer round trips and free of the
  check-then-act race condition the two-step version has under concurrent writers.

## Migrations

- One migration file per schema change, applied forward-only (no down-migrations to maintain) —
  name and order them so the applied sequence is unambiguous from the filename alone.
- A migration only changes schema/constraints/indexes. Data backfills that depend on application
  logic belong in application code, not in a migration script.
- Run migrations as a warm-up task: implement `IWarmUpCommand` (`BlogDoFT.Libs.WarmUp`) and register
  it with the DI container so `WarmUpHostedService` runs it, gating readiness (`WarmUpHealthCheck`)
  until it completes — not by blocking the host synchronously before it starts accepting
  connections.
  - Because multiple replicas can start this warm-up concurrently, the command itself must be safe
    under concurrent execution — take a Postgres advisory lock around the migration run, or make
    each migration idempotent/check-then-apply, so two instances starting at once don't race on the
    same schema change.

## Readability

- Format multi-line SQL as a readable block (one clause per line for anything beyond a trivial
  single-table select) with named parameters (`@paramName`), not positional placeholders — the
  next reader should be able to tell what a query does without cross-referencing the C# call site.

## Primary keys

- Primary key should be numeric (int/bigint) and auto-incrementing, unless there's a reason to use composite keys.
- To avoid brute force attacks, don't expose the primary key in public APIs. Use a separate unique identifier (e.g. UUID7) for external references.