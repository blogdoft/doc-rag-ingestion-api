# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

C# ASP.NET Core API responsible for ingesting and registering documents for a RAG (Retrieval-Augmented Generation) pipeline.

Implements `.specs/doc-rag-ingestion-api/SPEC.md`. See `README.md` for the architecture, decisions and operations.

## Commands

```bash
dotnet build DocRag.Ingestion.slnx
dotnet test tests/DocRag.Ingestion.Domain.Tests                 # fast, no I/O
dotnet test tests/DocRag.Ingestion.Application.Tests            # NSubstitute over ports
dotnet test tests/DocRag.Ingestion.Api.Tests                    # WebApplicationFactory with substituted ports
dotnet test tests/DocRag.Ingestion.Infrastructure.Tests         # Testcontainers (Docker): PostgreSQL, Garage, Kafka
docker compose -f .eng/docker/docker-compose.yml --env-file .env up --build
scripts/dev-token.sh                                            # local JWT
```

## Layout and rules

- `src/DocRag.Ingestion.{Domain,Application,Infrastructure,Api,Worker}`; dependencies point inward
  (Domain <- Application <- Infrastructure/Api/Worker). Tests mirror it under `tests/`.
- Domain: builders return `Result<T>` (BlogDoFT.Libs.ResultPattern + FluentValidation); failure codes start with the
  intended HTTP status (`400-upload-in-progress`) and are translated centrally in `Api/Problems/FailureResults`.
- Persistence is Dapper only (no EF Core): `*Table` row types, snake_case SQL, bigint PKs + public UUID v7,
  forward-only FluentMigrator migrations run as a warm-up task.
- Never read `ConnectionStrings` eagerly in service registration (tests override configuration later); use factories.
- Tests: xUnit + Shouldly + NSubstitute + Bogus, `Should_X_When_Y`. Integration tests use real Kestrel (TestServer buffers bodies).

## Conventions

- All documentation, code comments, commit messages and identifiers must be written in English, even though the user converses in Portuguese.
- Comment only non-obvious code and public members. Document public members with XML doc (`///`), never `//` comments.
