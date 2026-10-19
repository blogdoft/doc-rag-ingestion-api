# SPEC 01 — Doc RAG Ingestion API

**Status:** proposta de implementação · **Versão:** 1.0 · **Stack:** ASP.NET Core / C#, PostgreSQL, Garage (S3), Apache Kafka

## 1. Objetivo e limites

Implementar uma API HTTP para (a) cadastrar a identidade lógica de documentos, (b) criar versões editoriais, (c) receber arquivos originais via streaming, (d) armazená-los de forma durável no Garage, (e) persistir metadados e estado no PostgreSQL e (f) publicar eventos Kafka confiavelmente após ingestão bem-sucedida.

**Não implementar:** PDF→Markdown, OCR, conversão DOCX, extração estrutural, chunking, JSONL Document IR, embeddings, pgvector, pesquisa RAG, workflows de revisão e orquestração dos serviços downstream. O serviço emite o fato de que um arquivo foi recebido; consumidores executam o restante.

## 2. Requisitos funcionais

- RF-01: Criar documento com título, descrição opcional, tipo, idioma BCP 47, tags e metadata JSON extensível; gerar UUID v7 no servidor.
- RF-02: Consultar documento por identificador.
- RF-03: Atualizar metadados por JSON Merge Patch com `If-Match` e ETag de concorrência otimista.
- RF-04: Criar versão vinculada a documento, com `label`, publicação e vigência opcionais. Não inferir datas ausentes. Unicidade de `label` por documento.
- RF-05: Listar versões paginadas por cursor opaco e consultar uma versão e seu estado de ingestão.
- RF-06: Receber bytes do arquivo via corpo HTTP, sem Base64 e sem multipart, suportando streaming sem `Content-Length`.
- RF-07: Validar formato permitido, tamanho, nome de arquivo, integridade SHA-256 e políticas configuráveis; rejeitar upload vazio.
- RF-08: Armazenar objeto temporário no Garage, concluir a persistência e disponibilizar uma referência estável somente após sucesso.
- RF-09: Publicar evento `DocumentUploaded` no Kafka com Transactional Outbox; o evento deve ser idempotente para consumidores.
- RF-10: Suportar `Idempotency-Key` em operações de criação e upload; tentativas repetidas com mesmo conteúdo devolvem resultado anterior, sem eventos duplicados.
- RF-11: Expor erros RFC 9457 `application/problem+json`, com códigos HTTP apropriados e `traceId`.
- RF-12: Registrar trilha de auditoria mínima de operações e falhas sem incluir bytes do arquivo nem informações sensíveis em logs.

## 3. Contrato HTTP

**Fonte normativa:** [`openapi.yaml`](openapi.yaml). Swagger UI e o documento OpenAPI devem expor descrições extensas de operações, parâmetros, campos, formatos, exemplos, status HTTP, semântica de idempotência, upload e erros.

| Método | Caminho | Resultado |
|---|---|---|
| POST | `/api/v1/documents` | `201` documento |
| GET | `/api/v1/documents` | `200` página de documentos (filtros opcionais `type`, `language`, `tag`, `title` com wildcard `*`; `pageSize`, `cursor`) |
| GET | `/api/v1/documents/{documentId}` | `200` documento |
| PATCH | `/api/v1/documents/{documentId}` | `200` documento atualizado |
| POST | `/api/v1/documents/{documentId}/versions` | `201` versão |
| GET | `/api/v1/documents/{documentId}/versions` | `200` página |
| GET | `/api/v1/documents/{documentId}/versions/{versionId}` | `200` versão |
| POST | `/api/v1/documents/{documentId}/versions/{versionId}/files` | `201` arquivo recebido |

**Prefixo de publicação:** atrás de um proxy que remove um prefixo de roteamento (ex.: `/code-brain/doc-rag` no ingress do cluster), a URL pública é informada em `PublicBaseUrl`. Ela passa a ser o único `servers` do documento OpenAPI e o `PathBase` das requisições, de modo que o "Try it out" do Swagger UI, os redirecionamentos do `/swagger` e o header `Location` das respostas `201` incluam o prefixo (`{PathBase}/api/v1/...`). Sem `PublicBaseUrl` nada muda: o `Location` é relativo à raiz e o Swagger UI usa o esquema/host da própria requisição.

**Atenção:** `201` para upload significa arquivo recebido, armazenado e registrado com outbox no banco. **Não** significa que Kafka já entregou o evento nem que conversão/indexação terminaram. O nome do arquivo chega em `Content-Disposition`; tipos aceitos incluem PDF, Markdown, texto, HTML e DOCX, sujeitos a configuração.

### Exemplo de uso

```bash
curl -X POST https://ingestion.example.com/api/v1/documents \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -H 'Idempotency-Key: create-moc-cte' \
  -d '{"title":"Manual CT-e","type":"fiscal-manual","language":"pt-BR","tags":["cte"],"metadata":{"publisher":"ENCAT"}}'

curl -X POST "https://ingestion.example.com/api/v1/documents/$DOCUMENT_ID/versions" \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"label":"4.00"}'

curl --data-binary @manual.pdf -X POST \
  "https://ingestion.example.com/api/v1/documents/$DOCUMENT_ID/versions/$VERSION_ID/files" \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/pdf' \
  -H 'Content-Disposition: attachment; filename="manual.pdf"' \
  -H 'Idempotency-Key: upload-moc-cte-4-00'
```

## 4. Arquitetura interna

Separar Domain, Application, Infrastructure e API (Clean/Hexagonal sem abstrações desnecessárias). Referências de projetos: `DocRag.Ingestion.Domain`, `DocRag.Ingestion.Application`, `DocRag.Ingestion.Infrastructure`, `DocRag.Ingestion.Api`, `DocRag.Ingestion.Worker` (dispatcher Outbox e reconciliação). O worker pode inicialmente compartilhar deploy com a API, mas deve possuir entry point separado para escalabilidade.

**Interfaces na camada de aplicação:** `IDocumentRepository`, `IDocumentVersionRepository`, `IFileRepository`, `IObjectStorage`, `IOutboxRepository`, `IIdempotencyStore`, `IUnitOfWork`, `IClock`, `IEventPublisher`. Contratos de armazenamento devem ser streaming; não expor `byte[]` ou `MemoryStream` como única opção. Injeção de dependência, opções tipadas e validação na inicialização.

**Bibliotecas sugeridas:** Npgsql + EF Core para metadados, AWS SDK for .NET S3 para Garage, Confluent.Kafka para Kafka, OpenTelemetry para rastreamento, xUnit + NSubstitute + Shouldly + Testcontainers para testes. Preferir migrações explícitas e revisão de SQL. Evitar reflection em hot paths e evitar dependência direta de Garage no domínio.

## 5. Modelo relacional PostgreSQL

**`documents`**: `id uuid PK`, `title varchar(300)`, `description text NULL`, `type varchar(100)`, `language varchar(35)`, `tags jsonb`, `metadata jsonb`, `revision bigint NOT NULL DEFAULT 1`, `created_at timestamptz`, `updated_at timestamptz`. Índice único por identificador público.

**`document_versions`**: `id uuid PK`, `document_id uuid FK`, `label varchar(100)`, `published_at timestamptz NULL`, `effective_from timestamptz NULL`, `effective_to timestamptz NULL`, `ingestion_status varchar(30)`, `created_at timestamptz`, `updated_at timestamptz`; índice único `(document_id,label)` e FK para o documento. `effective_to >= effective_from` quando ambas presentes.

**`document_files`**: `id uuid PK`, `document_version_id uuid FK`, `original_name text`, `content_type text`, `size_bytes bigint`, `sha256 char(64)`, `bucket text`, `object_key text`, `storage_state text`, `uploaded_at timestamptz`, `created_at timestamptz`. Índice único para **um arquivo original concluído por versão** (parcial por tipo/estado, ou constraint equivalente). Nunca retornar `object_key` na API pública.

**`upload_attempts`**: `id uuid PK`, `document_version_id`, `status`, `temporary_object_key`, `lease_expires_at`, `bytes_received`, `failure_code`, `created_at`, `updated_at`. Índice/lock para impedir dois uploads ativos simultâneos.

**`outbox_messages`**: `id uuid PK`, `event_type`, `aggregate_id`, `payload jsonb`, `occurred_at timestamptz`, `published_at timestamptz NULL`, `attempts int`, `next_attempt_at timestamptz`, `last_error text NULL`; índice de mensagens pendentes. Usar `FOR UPDATE SKIP LOCKED` e lease de processamento, ou mecanismo equivalente.

**`idempotency_records`**: `operation`, `idempotency_key`, `request_fingerprint`, `response_status`, `response_body jsonb`, `resource_id`, `state`, `expires_at`, PK `(operation,idempotency_key)`. Para upload, fingerprint inclui identidade do recurso e SHA-256 efetivo; o sistema precisa reservar a chave antes de receber bytes e concluir a comparação ao final.

Definir convenção snake_case, `timestamptz` em UTC, constraints e FKs. Não usar `now() AT TIME ZONE 'UTC'` em colunas `timestamptz`; usar `now()` e serializar em UTC. Nunca armazenar o conteúdo binário no PostgreSQL.

## 6. Fluxo de upload e consistência

1. Autenticar, autorizar e validar existência da versão e estado permitido.
2. Reservar tentativa de upload com exclusão mútua por versão, associar idempotency key e TTL/lease; responder `400` a concorrência conflitante.
3. Abrir stream do corpo HTTP e transmitir incrementalmente ao Garage via cliente S3; computar SHA-256 e tamanho com contador, aplicar cancelamento e limite de bytes. SDK S3 pode exigir multipart upload interno para objetos grandes: usar partes de tamanho limitado, com memória constante; abortar multipart incompleto em falhas.
4. Verificar assinatura/formato real, hash esperado opcional, tamanho e integridade. Para inspeção de magic bytes, utilizar apenas pequeno buffer inicial. Não confiar em `Content-Type` nem em nome fornecido.
5. Finalizar objeto temporário e confirmar existência/atributos no Garage. Adotar chave opaca gerada pelo servidor (`document/version/file-id`), sem interpolar nomes originais. O objeto só deve ser considerado publicável após confirmação de commit do banco.
6. Na transação PostgreSQL, gravar `document_files`, transicionar a versão para `uploaded`, finalizar idempotência e inserir **uma** mensagem outbox `DocumentUploaded`. Commit atômico entre metadados e outbox.
7. Responder `201`. O dispatcher publica Kafka depois do commit. Se o commit falhar após o objeto ser gravado, a reconciliação identifica e remove/recupera objetos órfãos. Se Garage falhar, não persistir estado `uploaded` nem criar evento.
8. Implementar limpeza periódica de tentativas expiradas e multipart uploads abandonados. Falhas transitórias permitem retry; um upload finalizado não é sobrescrito.

**Ponto crítico:** Garage e PostgreSQL não participam da mesma transação. Usar máquina de estados e reconciliação eventual; não prometer atomicidade cross-system. Considerar comportamento de desconexão do cliente depois de o servidor concluir o commit: idempotência permite recuperar resultado sem duplicar.

## 7. Máquina de estados

`awaiting_upload -> uploading -> uploaded`; em falha, `uploading -> upload_failed`; nova tentativa pode ir de `upload_failed -> uploading`. `uploaded` é terminal para o arquivo original nesta versão. O estado `uploading` deve ser controlado por lease e recuperado após crash. Separar estado de `upload_attempts` do estado agregado da versão; não confiar em uma transição persistida para refletir upload ainda em andamento após falha de processo.

## 8. Eventos Kafka

Tópico configurável: `doc-rag.documents.uploaded.v1`. Chave da mensagem: `documentId` para manter ordenação por documento dentro da partição. Payload de evento em [`event-document-uploaded.schema.json`](event-document-uploaded.schema.json). Campos: `eventId`, `eventType`, `eventVersion`, `occurredAt`, `correlationId`, `documentId`, `versionId`, `fileId`, `contentType`, `sizeBytes`, `sha256`, `storage` com bucket e chave privada para consumidor autorizado.

**Garantia realista:** entrega **at-least-once**, não exactly-once fim a fim. Dispatcher pode publicar e cair antes de marcar `published_at`; consumidores devem deduplicar por `eventId`. Configurar `acks=all`, retries com backoff, timeouts e DLQ/alerta para falhas permanentes. Não incluir tokens, segredos ou URLs pré-assinadas nos eventos. Kafka deve ter TLS/SASL conforme ambiente. Definir política de retenção do tópico e compatibilidade evolutiva de schemas.

## 9. Segurança

**Multitenancy:** fora do escopo atual. O serviço não isola dados por tenant: não há claim de tenant, coluna `tenant_id` nem filtro por tenant. O limite de uploads simultâneos (`Upload:MaxConcurrentUploads`) é global. Quando o isolamento for necessário, deve ser reintroduzido de ponta a ponta (claim confiável do token, coluna e filtro em todas as consultas, chaves compostas e chave de objeto).

Validar assinatura JWT, issuer, audience, expiração, escopos. Bloquear path traversal, normalizar nomes, limitar metadados, tags e tamanho de JSON; proteger contra decompression bombs e arquivos com conteúdo incompatível. Bucket Garage privado, TLS, credenciais via Kubernetes Secrets ou gestor de segredos, permissões mínimas de bucket e rotação. Não expor caminhos internos nem mensagens detalhadas de dependências em erros públicos. Considerar varredura antimalware antes de liberar downstream, conforme classificação de risco.

## 10. Configuração

```json
{
  "ConnectionStrings": { "Postgres": "<secret>" },
  "Garage": {
    "ServiceUrl": "http://garage:3900", "Region": "garage", "Bucket": "doc-rag-originals",
    "AccessKey": "<secret>", "SecretKey": "<secret>", "ForcePathStyle": true
  },
  "Kafka": {
    "BootstrapServers": "kafka:9092", "TopicDocumentUploaded": "doc-rag.documents.uploaded.v1"
  },
  "Upload": {
    "MaxBytes": 1073741824, "AllowedContentTypes": ["application/pdf", "text/markdown", "text/plain", "text/html", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"],
    "MaxConcurrentUploads": 3, "LeaseMinutes": 30
  },
  "Outbox": { "BatchSize": 100, "PollingSeconds": 5, "MaxAttemptsBeforeAlert": 20 },
  "PublicBaseUrl": "https://blogdoft.home.arpa/code-brain/doc-rag"
}
```

`PublicBaseUrl` é opcional e só deve ser definido quando existir um prefixo removido pelo proxy (ver §3).

Valores são exemplos, não defaults mandatórios. Não comitar segredos reais. Validar existência e permissão do bucket no startup/readiness sem criar recursos implicitamente em produção. Configurar limites de corpo e timeouts no Kestrel **e** no Traefik/ingress; desabilitar buffering integral no proxy para endpoint de upload quando suportado. Monitorar uso de disco temporário, memória, multipart e conexões.

## 11. Observabilidade e operações

Instrumentar OpenTelemetry (trace IDs HTTP, operações DB, S3 e Kafka), métricas de duração/tamanho/taxa de upload, uploads ativos, falhas por motivo, idade e quantidade do outbox, retries Kafka, objetos temporários órfãos. Logs estruturados sem payloads nem credenciais. Health checks: liveness local, readiness para PostgreSQL e Garage (Kafka pode degradar dispatcher sem derrubar uploads se outbox saudável; alertar backlog). Shutdown gracioso e cancelamento propagado para streams. Manter dashboards e alertas de lag do outbox, capacidade Garage e falhas de upload.

## 12. Testes e critérios de aceite

Usar xUnit, NSubstitute e Shouldly; nomear testes `Should_<Expected>_When_<Scenario>`. Testcontainers para PostgreSQL, Kafka e Garage quando viável (ou ambiente de integração equivalente). Cobrir:

1. Cadastro e consulta com metadata arbitrário válido e validação de campos obrigatórios.
2. Duas versões com labels iguais no mesmo documento retornam `400`; labels iguais em documentos diferentes são permitidas.
3. PATCH com ETag antigo retorna `400`; sem If-Match retorna `400`.
4. Upload de arquivo grande mantém memória aproximadamente constante em relação ao tamanho do arquivo; verificar com testes de carga e medição real.
5. Upload sem `Content-Length`, streaming por chunks e cancelamento do cliente funcionam.
6. MIME proibido, assinatura inválida, arquivo vazio, tamanho excessivo e SHA-256 divergente são rejeitados com códigos definidos.
7. Dois uploads concorrentes da mesma versão não produzem dois arquivos concluídos.
8. Upload concluído grava arquivo, versão e outbox na mesma transação; falha do banco não produz evento consumível.
9. Falha do Garage não cria `uploaded` nem evento; tentativa é recuperável.
10. Repetição com mesma idempotency key e conteúdo devolve resultado original; conteúdo diferente gera `400`.
11. Dispatcher retenta publicação Kafka e consumidores conseguem deduplicar por `eventId`.
12. Crash após publicação e antes de marcar outbox não provoca perda de evento (duplicação é permitida).
13. Processo de reconciliação identifica e trata objetos órfãos, leases expirados e multipart abandonados.
14. Swagger UI documenta todos os endpoints, exemplos, códigos de resposta e autenticação; OpenAPI é validado em CI.
15. Não há dependência de conversão, chunking ou embeddings nos projetos de ingestão.

## 13. Entregáveis obrigatórios

- Solution .NET com camadas e testes; API e dispatcher worker.
- Migrações PostgreSQL, índices, constraints e documentação de rollback.
- Cliente Garage S3 com streaming e limpeza de multipart.
- Publicador Kafka e Transactional Outbox com retry.
- OpenAPI 3.1 e Swagger UI com XML comments / descrições ricas, exemplos e esquemas.
- JSON Schema do evento e amostras de payloads.
- Dockerfile multi-stage para API e worker, `docker-compose` para desenvolvimento (PostgreSQL, Garage, Kafka), `.env.example` sem segredos reais.
- README de setup, configuração de Garage, criação do bucket, testes, curl e troubleshooting.
- Pipeline CI com build, lint/analyzers, testes, validação do OpenAPI e varredura de dependências.
- Publicação e deploy (Forgejo Actions), ver §16.

## 14. Regras para o agente implementador

Priorizar SOLID, DRY, YAGNI, código assíncrono e cancellation tokens; não introduzir funcionalidades de processamento documental. Não inventar endpoints além do contrato sem registrar decisão. Usar ProblemDetails e validação consistente. Preservar a separação entre evento de domínio e modelo HTTP. Manter handlers finos e lógica em application services. Implementar recursos de concorrência/idempotência de verdade, não apenas declarar interfaces. Entregar código compilável, migrações executáveis, testes automatizados e instruções reproduzíveis. Documentar trade-offs quando houver limitações de SDK/infraestrutura.

## 15. Decisões ainda configuráveis

- Tamanho máximo por upload, lista final de formatos e política antimalware.
- Provedor de identidade e nomes concretos de claims/scopes.
- Política de retenção dos arquivos originais, cleanup e prazos de idempotência.
- Estratégia de event schema registry e tópicos por ambiente.
- Necessidade futura de upload retomável, URL pré-assinada e suporte a múltiplos arquivos originais por versão.

Essas decisões não impedem a implementação do MVP com os contratos definidos.

## 16. Publicação, deploy e espelhamento

Segue o padrão do `code-ciir-indexer`. Workflows em `.forgejo/workflows/`:

- **`ci.yml`:** build, testes, validação do OpenAPI e varredura de dependências em push para `main` e PRs.
- **`docker-publish.yml`:** disparado por tag `vX.Y.Z`. O job `test` calcula a versão com GitVersion (`.config/dotnet-tools.json`) e executa build e testes (`-m:1`, com uma nova tentativa para contenção de Testcontainers). O job `publish` constrói e publica duas imagens a partir do mesmo Dockerfile, `--target api` e `--target worker`, com as tags `<versão>` e `latest` (a do worker usa o sufixo `-worker` em `REGISTRY_IMAGE`). O job `update-gitops` carimba as imagens com `kustomize edit set image` e copia `.eng/k8s/` para `manifests/code-brain/doc-rag-ingestion-api/` em `sauron/argo-local-apps`, de onde o ArgoCD aplica no k3s.
- **`mirror-to-github.yml`:** espelha todas as branches e tags para o GitHub diariamente às 20h (America/Sao_Paulo), com retry.

Variáveis do repositório: `REGISTRY`, `REGISTRY_IMAGE`, `REGISTRY_USERNAME`. Segredos: `REGISTRY_PASSWORD`, `ARGO_DEPLOY_SSH_KEY` (deploy key dedicada com escrita), `MIRROR_GITHUB_TOKEN`.

Manifestos em `.eng/k8s/` (kustomize): `configmap.yaml` (configuração não secreta compartilhada), `api-deployment.yaml`, `api-service.yaml`, `worker-deployment.yaml`, `ingress.yaml` e `middleware.yaml`. Probes em `/health/ready` e `/health/live`. Os Secrets `doc-rag-ingestion-secrets` (`connection-string`) e `doc-rag-ingestion-garage-secrets` (`access-key`, `secret-key`) não são gerenciados no repositório.

**Roteamento:** o Ingress (Traefik, `websecure`, TLS `blogdoft-tls`) publica o host compartilhado `blogdoft.home.arpa` em `/code-brain/doc-rag`. O Middleware `stripPrefix` remove esse prefixo antes de encaminhar, e `PublicBaseUrl` informa o prefixo à aplicação (§3). Nenhum middleware de buffering é aplicado, para que os uploads de até `Upload:MaxBytes` sejam transmitidos em streaming.
