---
name: csharp-worker
description: Coding rules for C# background workers / hosted services — BackgroundService lifecycle, graceful shutdown, retry/backoff for transient failures, idempotency, and structured logging for long-running or scheduled processes. Use this whenever writing, reviewing, or editing a `BackgroundService`/`IHostedService`, a scheduled or continuously-running processing loop, a queue/stream consumer, or a batch job runner — even for something as small as "add a retry when the embedding call fails" or "make this run every N minutes".
---

# C# background worker coding standards

Rules for long-running/scheduled processes (`BackgroundService`-based workers, queue consumers,
batch runners). Unlike a request-driven adapter, a worker owns its own lifecycle and has to behave
correctly across restarts, transient outages, and shutdown signals with nobody watching in real
time.

## Lifecycle and shutdown

- Inherit `BackgroundService` and do the real work in `ExecuteAsync`, threading the provided
  `CancellationToken` into every downstream async call (DB queries, HTTP calls, delays).
- Treat `OperationCanceledException` raised by that token during shutdown as the expected signal
  to stop, not as an error to log at error level or retry — the host is telling the worker to wind
  down.
- Don't block synchronously in `ExecuteAsync` (no `.Result`/`.Wait()`) — a sync-over-async worker
  loop can deadlock the host's shutdown sequence.
- When a `BackgroundService` is stopped as part of an ordinary host shutdown, don't try to restart
  it or requeue its work — just finish what you can and exit cleanly. This is the graceful-shutdown
  counterpart to the Idempotency rules below, which cover the abrupt-kill case where nothing gets a
  chance to finish.

## Fault isolation

- Wrap each unit of work inside the processing loop (one item, one batch, one iteration) in its
  own try/catch that logs the failure and continues to the next unit, rather than letting one bad
  item bring down the whole worker. Only let an exception propagate out of `ExecuteAsync` when the
  failure is genuinely unrecoverable (e.g. the process can't get a DB connection at all) — that's
  what should actually stop the host.

## Idempotency

- Because a worker can be killed and restarted (or retry the same unit of work) at any point,
  design each unit of work so reprocessing it is safe — prefer upserts keyed by a stable identity
  over blind inserts, and prefer comparing a content hash/version before redoing expensive work
  (embedding generation, external calls) over unconditionally redoing it every run.
- Don't rely on in-memory state to track "have I already done this" across restarts — that state
  has to be recoverable from the persisted data itself.

## Retry and backoff

- Distinguish transient failures (network blip, DB momentarily unavailable, rate limit) from
  permanent ones (malformed input, auth failure) — only the former should be retried.
- Use exponential backoff with a capped number of attempts for transient failures, not a tight
  retry loop — a tight loop on a struggling downstream dependency makes the outage worse.
- After exhausting retries, record the failure (log +, if the domain model supports it, a
  persisted failed/needs-attention state) and move on to the next unit of work rather than stalling
  the whole run on one item.

## Logging and observability

- Log with a run/batch correlation identifier so every line belonging to one execution can be
  traced together, especially across the fault-isolation try/catch blocks above.
- Log at a level proportional to actionability: routine progress at `Information`/`Debug`,
  a single-item failure that was retried/skipped at `Warning`, a failure that stopped the whole
  worker at `Error`.

## Configuration

- Bind worker settings (interval, batch size, retry policy) through `IOptions<T>`, not static
  fields or values read directly from `IConfiguration` scattered through the worker body — this
  keeps settings testable and centrally validated at startup.
