# Doc RAG Ingestion API

ASP.NET Core service that **ingests and registers documents** for the Doc RAG platform: logical
document identity, editorial versions, streaming reception of the original file, durable storage in
Garage (S3), state in PostgreSQL and a reliable `DocumentUploaded` event on Kafka (transactional
outbox). It deliberately does **not** convert, chunk, embed or search anything - downstream
consumers react to the event.

Normative contract: [`.specs/doc-rag-ingestion-api/`](.specs/doc-rag-ingestion-api/SPEC.md). The running
service serves its OpenAPI document and Swagger UI at `/swagger`.

## Layout

| Project | Role |
|---|---|
| `DocRag.Ingestion.Domain` | Entities, builders (FluentValidation, `Result<T>`), state transitions, domain event. No I/O. |
| `DocRag.Ingestion.Application` | Use cases, ports (`IObjectStorage`, `IOutboxRepository`, ...), idempotency, upload orchestration, outbox dispatcher, reconciler. |
| `DocRag.Ingestion.Infrastructure` | Dapper repositories, FluentMigrator migrations (run as a warm-up task), Garage S3 client, Kafka publisher, health checks. |
| `DocRag.Ingestion.Api` | Controllers, DTOs, auth, problem-details mapping, Swashbuckle, OpenTelemetry. |
| `DocRag.Ingestion.Worker` | Separate entry point: outbox dispatcher + reconciliation loops (scale independently of the API). |

## Quick start

```bash
cp .env.example .env
docker compose -f .eng/docker/docker-compose.yml --env-file .env up --build
# Swagger UI:  http://localhost:8080/swagger      readiness: http://localhost:8080/health/ready
```

The compose file starts PostgreSQL, Garage (plus a one-shot job that imports the access key and creates
the private bucket), Kafka, the API (applies migrations on start-up) and the worker.

Without Docker for the apps (infrastructure from compose, apps from source):

```bash
docker compose -f .eng/docker/docker-compose.yml --env-file .env up -d postgres garage garage-setup kafka
ASPNETCORE_URLS=http://localhost:8080 dotnet run --project src/DocRag.Ingestion.Api
ASPNETCORE_URLS=http://localhost:8081 dotnet run --project src/DocRag.Ingestion.Worker
```

### Try it

```bash
TOKEN=$(scripts/dev-token.sh acme)        # HS256 token for local development only
B=http://localhost:8080/api/v1

DOC=$(curl -s -X POST $B/documents -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -H 'Idempotency-Key: create-moc-cte' \
  -d '{"title":"Manual CT-e","type":"fiscal-manual","language":"pt-BR","tags":["cte"],"metadata":{"publisher":"ENCAT"}}' | jq -r .id)

VER=$(curl -s -X POST $B/documents/$DOC/versions -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' -d '{"label":"4.00"}' | jq -r .id)

curl --data-binary @manual.pdf -X POST "$B/documents/$DOC/versions/$VER/files" \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/pdf' \
  -H 'Content-Disposition: attachment; filename="manual.pdf"' -H 'Idempotency-Key: upload-moc-cte-4-00'

curl -s $B/documents/$DOC/versions/$VER -H "Authorization: Bearer $TOKEN" | jq .status   # "uploaded"
```

PATCH needs the current ETag: `curl -i $B/documents/$DOC` returns `ETag: "1"`, then send
`-H 'If-Match: "1"' -H 'Content-Type: application/merge-patch+json' -X PATCH -d '{"description":"..."}'`.

## Running locally

Prerequisites: .NET SDK 10 and Docker (compose plus the Testcontainers-based tests).

1. `cp .env.example .env` - development-only defaults, never commit the copy.
2. Start everything in containers (see [Quick start](#quick-start)) **or** only the infrastructure and run the
   apps from source. The projects do not pin a port, so set one explicitly, e.g.
   `ASPNETCORE_URLS=http://localhost:8080 dotnet run --project src/DocRag.Ingestion.Api` and
   `ASPNETCORE_URLS=http://localhost:8081 dotnet run --project src/DocRag.Ingestion.Worker`.
3. Get a token with `scripts/dev-token.sh`. It is signed with `AUTH_SIGNING_KEY` from `.env`, which the
   compose file passes to the API as `Auth__SigningKey`; if you changed it, export the same value before calling the script.
4. Open Swagger UI at `http://localhost:8080/swagger`, click **Authorize** and paste the token without the `Bearer ` prefix.
5. Run the tests: `dotnet test DocRag.Ingestion.slnx` (the Infrastructure tests need Docker).

`PublicBaseUrl` stays unset locally, so Swagger UI and the `Location` header use the request's own scheme and host.

## Cluster deployment (k3s)

Pushing a `vX.Y.Z` tag runs `.forgejo/workflows/docker-publish.yml`: tests, image build/push (`api` and
`worker`) and a commit of `.eng/k8s/` to `sauron/argo-local-apps`, which ArgoCD applies. The API is then served at
`https://blogdoft.home.arpa/code-brain/doc-rag` (Swagger UI under `/swagger`). The workflow needs the Forgejo
variables `REGISTRY`, `REGISTRY_IMAGE`, `REGISTRY_USERNAME` and the secrets `REGISTRY_PASSWORD`, `ARGO_DEPLOY_SSH_KEY`
and `MIRROR_GITHUB_TOKEN` (GitHub mirror).

### Secrets to create in the cluster

The Deployments read two Secrets that are **not** stored in the repository. Create them once in the namespace the
ArgoCD application deploys into (`code-brain`):

| Secret | Keys | Content |
|---|---|---|
| `doc-rag-ingestion-secrets` | `connection-string` | Npgsql connection string for the ingestion database. |
| `doc-rag-ingestion-garage-secrets` | `access-key`, `secret-key` | Garage S3 key (id `GK` + 24 hex chars, secret 64 hex chars) with access to the `doc-rag-originals` bucket. The bucket is never created by the service. |

Example values (replace everything; these are placeholders, not real credentials):

```bash
export POSTGRES_CONNECTION_STRING='Host=postgres.databases.svc.cluster.local;Port=5432;Database=docrag_ingestion;Username=docrag;Password=change-me'
export GARAGE_ACCESS_KEY='GK000000000000000000000001'
export GARAGE_SECRET_KEY='0000000000000000000000000000000000000000000000000000000000000001'

scripts/create-cluster-secrets.sh code-brain      # namespace defaults to code-brain
```

The script wraps `kubectl create secret generic ... --dry-run=client -o yaml | kubectl apply -f -`, so it is safe to
re-run to rotate a value; restart the Deployments afterwards (`kubectl rollout restart deployment -n code-brain
doc-rag-ingestion-api doc-rag-ingestion-worker`) because environment variables are read at start-up. Non-secret
settings (Keycloak authority, Kafka bootstrap servers, Garage URL, OTLP endpoint) live in `.eng/k8s/configmap.yaml`;
review them before the first deploy.

## Configuration

Bound from `appsettings*.json` / environment (`Section__Key`), validated at start-up (the host refuses
to start on invalid values). Secrets always come from the environment or a secret manager - never commit them.

| Key | Meaning |
|---|---|
| `ConnectionStrings:Postgres` | PostgreSQL connection string (secret). |
| `Auth:Authority` / `Auth:Audience` / `Auth:Issuer` | JWT validation against an identity provider (production). |
| `Auth:SigningKey` | Symmetric HS256 key, **development/tests only**. Exactly one of `Authority` or `SigningKey` must be set. |
| `Auth:SwaggerClientId` / `Auth:SwaggerScopes` | Optional. With `Authority` set, Swagger UI's **Authorize** redirects to the identity provider (authorization code + PKCE, public client). Scopes default to `openid` + the read/write scopes. The client must allow `<public-url>/swagger/oauth2-redirect.html` as a redirect URI. |
| `Auth:ReadScope`, `Auth:WriteScope` | Required scopes. Write implies read. |
| `Garage:ServiceUrl/Region/Bucket/AccessKey/SecretKey/ForcePathStyle/PartSizeMiB` | S3 access. The bucket is **never created** by the service; readiness fails if it is not accessible. `PartSizeMiB` (5-512) bounds memory per upload. |
| `Upload:MaxBytes/AllowedContentTypes/MaxConcurrentUploads/LeaseMinutes/IdempotencyTtlHours` | Upload policy. |
| `Kafka:BootstrapServers/TopicDocumentUploaded/SecurityProtocol/Sasl*` | Producer (worker only): `acks=all`, idempotent producer. Use `SaslSsl` outside development. |
| `Outbox:BatchSize/PollingSeconds/MaxAttemptsBeforeAlert/LeaseSeconds` | Dispatcher. |
| `Reconciliation:IntervalSeconds/MultipartMaxAgeMinutes/CleanupBatchSize` | Reconciler. |
| `PublicBaseUrl` | Optional externally reachable base URL (e.g. `https://blogdoft.home.arpa/code-brain/doc-rag`). Becomes the OpenAPI `servers` entry and the path base for Swagger UI behind a prefix-stripping proxy. |
| `Observability:*`, `OTEL_SERVICE_NAME` | `BlogDoFT.Libs.Api.OpenTelemetry` exporters (OTLP/Zipkin/Prometheus/console). |

**Request body limits:** Kestrel's global limit equals `Upload:MaxBytes`; the JSON endpoints (create/patch
document, create version) narrow it to 1 MiB with `[RequestSizeLimit]` and answer `413` as a problem document
beyond it. The global limit is deliberately not low: if an early rejection (an expired token, say) leaves a body
larger than the limit unread, Kestrel cannot drain it and closes the connection, and a reverse proxy that is still
sending the body then reports `502 Bad Gateway` instead of the real `401`/`403`.

**Reverse proxy / ingress:** the proxy must be configured too: raise (or remove) the body-size limit, extend read/idle timeouts and disable
request buffering for `/api/v1/documents/*/versions/*/files` (Traefik: `maxRequestBodyBytes`, no
`buffering` middleware on that route; ingress-nginx: `proxy-body-size`, `proxy-request-buffering: off`).

## How it works

**Upload** (`FileUploadService`): (1) in one transaction lock the version, expire dead leases, enforce one
active attempt per version and the global concurrent-upload limit, reserve the idempotency key, create an
`upload_attempts` row with a lease and mark the version `uploading`; (2) stream the body to Garage - peek
the first 8 KiB to check the real file signature, then multipart-upload with parts of `PartSizeMiB` while
counting bytes and computing SHA-256, so memory stays constant; (3) verify checksum and stored size;
(4) in a second transaction insert the file, set the version `uploaded`, finish the idempotency record
and insert **one** outbox message. Garage and PostgreSQL cannot share a transaction, so every failure path
is handled by the attempt state machine and by the reconciler:

| Failure | Result |
|---|---|
| Garage fails / client disconnects / checksum mismatch / too large | Attempt `failed`, version `upload_failed`, object deleted, key released. Retry allowed. No file row, no event. |
| Process dies mid-upload | Lease expires; reconciler marks the attempt `expired`, the version `upload_failed`, deletes the orphan object and aborts abandoned multipart uploads. |
| PostgreSQL commit fails after the object was written | Nothing is registered; the attempt's lease expires and the object is reclaimed. |
| Dispatcher publishes then dies before `published_at` | Message is re-claimed after its lease and re-published: **at-least-once**, consumers deduplicate by `eventId`. |

**Outbox:** `FOR UPDATE SKIP LOCKED` + processing lease, exponential backoff (5 s doubling, cap 15 min).
After `MaxAttemptsBeforeAlert` failures an `ALERT:` error is logged and the message keeps being retried.
Ordering per document is preserved by the `documentId` key on a healthy broker; a failed message
that is retried later can be overtaken by newer ones (acceptable for idempotent consumers).

**Event contract:** [`docs/events/event-document-uploaded.schema.json`](docs/events/event-document-uploaded.schema.json),
sample in [`docs/events/document-uploaded.sample.json`](docs/events/document-uploaded.sample.json). The payload carries the
bucket and a private object key - no tokens or pre-signed URLs.

## Decisions that deviate from, or complete, the spec

* **`GET /api/v1/documents` lists documents** (not in the original contract; registered in
  `openapi.yaml`): newest first by UUID v7, opaque keyset cursor, optional exact-match `type`, `language` and `tag`
  filters combined with AND, plus a case-insensitive `title` search where `*` is the wildcard (`manual*`, `*cte`, `*fiscal*`;
  no `*` means equality with the whole title; `%` and `_` are literal). No trigram index yet: add `pg_trgm` + GIN if titles grow large. Page size is 1-100 (default 20), shared with the version listing.
* **Dapper, not EF Core** (project standard): explicit SQL, `*Table` row types with `ToDomain/FromDomain`.
* **Numeric PK + public UUID v7**: tables use `bigint` identity keys; the API only ever exposes `public_id`
  (UUID v7).
* **401, 403, 404 and 5xx have no body** (project convention; they are logged, 403 with the username).
  Every other client error is `application/problem+json` (RFC 9457) with `traceId`.
* **Idempotent replay returns the current representation** of the resource the first request created
  (looked up by `resource_id`) instead of a stored response body. Uploads compare the SHA-256 of the
  replayed body against the original; the body is drained (hashed, not stored) to do so.
* **Objects are written directly to their final opaque key** (`document/version/fileId`) and become
  referenced only on commit - no copy step; unreferenced objects are reconciled.
* **Leases are not renewed**: an upload slower than `Upload:LeaseMinutes` is rejected with 409 at the end.
  Size `LeaseMinutes` for the slowest expected transfer.
* **Kafka is not part of readiness**: an unreachable broker grows the outbox backlog (metrics
  `docrag.outbox.pending`, `docrag.outbox.oldest_age`) without taking uploads down.
* **Content validation** checks the real signature (`%PDF-`, ZIP magic for DOCX, strict UTF-8 without NUL for
  text types). The service never decompresses or renders files; run antimalware/decompression-bomb scanning
  in the downstream converters or add a scanning step before publishing if your risk classification requires it.
* Local tokens are HS256 (`scripts/dev-token.sh`); production must use `Auth:Authority`.

## Tests

```bash
dotnet test tests/DocRag.Ingestion.Domain.Tests         # entities, builders, merge patch
dotnet test tests/DocRag.Ingestion.Application.Tests    # use cases against substituted ports
dotnet test tests/DocRag.Ingestion.Api.Tests            # HTTP pipeline, auth, problem details, OpenAPI
dotnet test tests/DocRag.Ingestion.Infrastructure.Tests # Testcontainers: PostgreSQL 16, Garage, Kafka (needs Docker)
```

The integration suite starts real PostgreSQL, **Garage** and Kafka containers (images `postgres:16`,
`dxflrs/garage:v2.4.1`, `confluentinc/cp-kafka:7.5.12`) and a real Kestrel host; it covers
label uniqueness, ETags, concurrent uploads, constant-memory streaming of a 300 MiB file, chunked bodies,
client cancellation, atomic file+outbox commits, Garage failures, idempotency, `SKIP LOCKED` claiming,
publish-then-crash redelivery and reconciliation.

## Operations

* Health: `/health/live` (process), `/health/ready` (migrations done, PostgreSQL, Garage bucket). Worker: same paths.
* Metrics (meter `DocRag.Ingestion`): `docrag.uploads.{total,failures,active,bytes,duration}`,
  `docrag.outbox.{published,failures,pending,oldest_age}`, `docrag.reconciliation.orphans_cleaned`.
  Alert on `oldest_age`, rising `failures`, and Garage capacity.
* Logs: JSON on stdout with scopes (`CorrelationId`); never payloads or credentials. Send `X-Correlation-Id`
  to correlate; it is echoed, logged, tagged on the trace and copied into the event.
* Database rollback: migrations are forward-only; see [`docs/ROLLBACK.md`](docs/ROLLBACK.md).

## Troubleshooting

| Symptom | Check |
|---|---|
| `/health/ready` unhealthy right after start | Warm-up migrations still running (normal for a few seconds) or PostgreSQL unreachable. |
| Readiness fails on `garage` | Bucket missing or key without permission: run `.eng/docker/garage/setup.sh` (dev) / ask the platform team (prod). The service never creates buckets. |
| Upload returns `503` | Garage unreachable or credentials wrong; see the `Object storage failed` log with the attempt id. |
| Upload returns `409 upload-in-progress` after a crash | The previous lease must expire (`Upload:LeaseMinutes`) or the reconciler must run; the worker does this every `Reconciliation:IntervalSeconds`. |
| Upload stops at 1 GiB / hangs behind a proxy | Proxy body limit/timeouts/buffering (see above). |
| Events not arriving | `docrag.outbox.pending` growing: worker not running or Kafka unreachable (`ALERT:` log lines). |
| `401` on every call | Token audience/issuer/signature/expiry; the reason is only in the server log. |
| `403` on a valid token | Missing `ingestion:read` / `ingestion:write` scope. |
