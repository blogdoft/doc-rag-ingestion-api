---
name: csharp-cli
description: Coding rules for C# command-line driving adapters — argument parsing, exit codes, stdout/stderr discipline, and pipe-friendly output. Use this whenever writing, reviewing, or editing a CLI entry point (`Main`), command/argument definitions, or how a console app reports success/failure to its caller — even for something as small as "add a --dry-run flag" or "what exit code should this return".
---

# C# CLI coding standards

Rules for command-line driving adapters — the layer that turns a shell invocation into a call into
the Application layer's use case, and turns the result back into exit code + text.

## Argument parsing

- Parse arguments with a proper parsing library (e.g. `System.CommandLine`) rather than indexing
  into `args[]` by hand. A real parser gives consistent `--help` output, type conversion, and
  error messages for free, and makes adding a new flag a small diff instead of a rewrite of ad hoc
  parsing logic.
- Validate required arguments as part of parsing (the parser rejects a missing required argument
  before the command handler runs), not as an `if (arg == null)` check buried inside the handler.

## Exit codes

- Return `0` only on success. Use distinct non-zero codes for distinct failure classes when a
  caller (a script, CI job) would plausibly want to branch on *why* it failed — e.g. invalid
  arguments vs. a processing failure vs. an unreachable dependency — rather than collapsing every
  failure to a bare `1`.
- Keep the exit-code mapping in one place (where the top-level `Result` from the Application-layer
  use case is translated), not scattered across multiple `Environment.Exit` calls.

## Output discipline

- Write the command's actual result/output to `stdout`. Write logs, progress messages, and
  diagnostics to `stderr`. This is what lets the tool compose in a pipeline (`| grep`, `| jq`, output
  redirection) without progress noise corrupting the piped data.
- Don't prompt interactively for required input by default — accept it as a flag/argument, so the
  same command works unattended in a script or CI job. If an interactive prompt is genuinely
  useful for a human running it directly, gate it behind detecting an interactive terminal or an
  explicit opt-in flag, and always provide the non-interactive path.

## Keep the entry point thin

- `Main`/the command handler should: parse and validate arguments → build/resolve the
  Application-layer use case → invoke it → translate its `Result` into exit code and
  stdout/stderr output. Business logic, orchestration, or I/O beyond that translation belongs in
  the Application layer, not in the CLI adapter — this is what lets a future Web API adapter reuse
  the same use case without duplicating logic.
