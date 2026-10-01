# MyBudget-bot — Working Context

Single source of truth for how this repo is built and changed. Keep it in sync when
architecture, scripts, conventions or status change. Details live in
[`docs/TECHNICAL-DESIGN.md`](docs/TECHNICAL-DESIGN.md); the next task and the backlog live in
[`docs/HANDOFF.md`](docs/HANDOFF.md).

## What this project is

A personal budgeting Telegram bot for a Colombian user: fast expense entry, reliable
reporting. Money is whole COP pesos (`long` / `bigint`), user-facing text is Spanish,
everything technical is English. Modular monolith on the `santidev21` VPS behind
`vps-gateway`.

## Non-negotiable rules

1. **Language separation.** Code, identifiers, namespaces, table and column names, config keys,
   log event names, test names, comments, docs and commit messages are **English**. Only
   user-facing strings are Spanish, and they live in resources — never inline in business
   logic, never in the domain or application layers.
2. **Money is exact.** `long` only. Never `float`, `double`, `decimal` persisted, or formatted
   strings. Whole pesos: `35.000 COP` is `35000`.
3. **Expense dates are calendar dates.** `ExpenseDate` is `DateOnly` / `date`, resolved in the
   user's time zone. `CreatedAt` / `UpdatedAt` are UTC instants (`timestamptz`). Getting this
   wrong moves expenses into the wrong month.
4. **Ownership is enforced twice.** Every scoped repository query takes `userId` first (a
   missing scope is a compile error), and composite foreign keys enforce it in PostgreSQL.
5. **History is indestructible.** A category with expenses or a funded allocation cannot be
   deleted. Deactivate instead. User erasure goes through `IUserDataEraser`.
6. **No business logic in Telegram handlers.** Handlers parse, call an application use case,
   format, and send.
7. **No AI for categorization.** Deterministic matching only.
8. **Never edit an applied migration.** Add a new one.

## Repository layout

```
MyBudget.sln
Directory.Build.props          # shared build settings
Directory.Packages.props       # central package versions (single source of truth)
src/
  MyBudget.Domain/             # entities, value objects, pure math. No dependencies.
  MyBudget.Application/        # use cases + persistence abstractions. Domain only.
  MyBudget.Infrastructure/     # EF Core, Npgsql, migrations, repositories.
  MyBudget.Telegram/           # presentation: dispatch, conversations, charts, rendering.
  MyBudget.Api/                # composition root, health, Serilog, --migrate.
tests/                         # Domain, Application, Infrastructure, Telegram, Architecture
docker/app/Dockerfile
docker/postgres/init/          # least-privilege role provisioning (runs once)
scripts/deploy.sh              # deploy, check, rollback
scripts/backup.sh              # nightly verified backup
scripts/verify-backup.sh       # restore drill into a throwaway database
scripts/restore.sh             # break-glass restore into the live database
scripts/install-backup-cron.sh # installs the nightly and weekly cron entries
docs/TECHNICAL-DESIGN.md
docs/BACKUPS.md                # backup, verification and restore runbook
docs/DEPLOYMENT.md             # deploy, rollback and operations
docs/HANDOFF.md                # status, next task prompt and backlog
docs/adr/                      # one file per irreversible decision
docs/specs/                    # focused specs (implementation gotchas, ...)
```

## Documentation Policy

Docs capture decisions and current state, never session narration.

- **Allowed:** this file, `README`, `docs/TECHNICAL-DESIGN.md`, `docs/adr/NNN-*.md` (one decision:
  context, options, decision, consequences), `docs/specs/*.md` (focused specs such as
  `gotchas.md`), the runbooks (`BACKUPS`, `DEPLOYMENT`) and `docs/HANDOFF.md`.
- **Forbidden:** phase reports, progress logs, "what I did" narration and per-session summaries.
  When a change needs a durable record, update the design doc or add an ADR — do not create a
  report file. This applies to AI output too.

## Commands

| Task | Command |
|---|---|
| Build | `dotnet build MyBudget.sln` |
| All tests | `dotnet test MyBudget.sln` |
| Unit tests only (no Docker) | `dotnet test tests/MyBudget.Domain.Tests` |
| Format check | `dotnet format MyBudget.sln --verify-no-changes` |
| Add a migration | `dotnet dotnet-ef migrations add <Name> --project src/MyBudget.Infrastructure --startup-project src/MyBudget.Infrastructure` |
| Apply migrations | `dotnet dotnet-ef database update --project src/MyBudget.Infrastructure --startup-project src/MyBudget.Infrastructure` |
| Local stack | `docker compose -f docker-compose.yml -f docker-compose.local.yml up --build` |
| Local API | `http://localhost:8092` (native) or `http://localhost:8091` (Docker) |
| Local database | `127.0.0.1:5435` (5432/5433/5434 are taken on this host) |
| Fill Telegram settings | `./scripts/init-telegram-env.sh --polling` (never prints secrets) |
| Register the bot | `dotnet run --project src/MyBudget.Api -- --configure-telegram` |
| Remove the webhook | `dotnet run --project src/MyBudget.Api -- --delete-webhook` |
| Deploy / backup / drill | `cd /opt/mybudget && ./scripts/deploy.sh deploy` · `./scripts/backup.sh` · `./scripts/verify-backup.sh` |

Local chat development: the app reads `.env` in development, so `TELEGRAM_BOT_TOKEN`,
`ALLOWED_TELEGRAM_USER_IDS` and `TELEGRAM_USE_POLLING=true` are enough, then
`dotnet run --project src/MyBudget.Api`. Delete any registered webhook first — Telegram allows
webhooks or polling, never both. Polling reuses the same dispatcher as the webhook.

Containers `mybudget` / `mybudget-db` / `mybudget-migrator`; networks `mybudget-net` (external,
gateway) and `mybudget-internal-net` (internal, database only). No ports published in
production.

## Testing conventions

- Integration tests use **Testcontainers + real PostgreSQL**. Never the EF in-memory provider:
  it cannot enforce CHECK, UNIQUE or FOREIGN KEY constraints, which is what those tests verify.
- The shared `DatabaseFixture` migrates once and Respawn resets between tests.
- Raw SQL is used deliberately when a test must produce a state the domain forbids (empty
  names, negative amounts, cross-user references), proving the database refuses it.
- Architecture tests fail the build if the layering is violated.
- Test method names read as sentences; no `Method_Should_Do_Thing` noise.

## Gotchas discovered the hard way

The full list, with the reasoning behind every entry, lives in
[`docs/specs/gotchas.md`](docs/specs/gotchas.md). The load-bearing points:

- **EF Core:** aggregate-created records need store-generated keys; never make the `NO ACTION`
  composite FKs `DEFERRABLE`; `ExecuteDeleteAsync` bypasses the change tracker.
- **Configuration and hosting:** binding never turns a single value into an array; hosted
  services are singletons (open a scope per iteration); the local compose overlay needs
  `external: false`.
- **Text and payloads:** Spanish is the neutral resource set and every key must be in
  `MessageKeys`; conversation payloads are `jsonb` (compare as data); callback data is capped at
  64 bytes; a confirmation is claimed once with a conditional `UPDATE`.
- **Dates, money and reporting:** `IUserLocalDate` is the only UTC-to-local conversion; the
  matcher's 0.20 partial-overlap floor is load-bearing; a budget-alert marker is recorded whether
  or not the notification is delivered.
- **Operations:** quote `"__EFMigrationsHistory"` in raw SQL; `pg_restore` needs
  `--exit-on-error` to actually fail.
- **Recurring, closing and charts:** `last_generated_date` and the generated expense commit in
  the same transaction; the monthly closing and the daily reminder claim their marker before
  sending; charts are hand-drawn PNGs and a bar is a share of the total it is given.
- **Settings, budgets and routing:** `IUserDataEraser` reuses the turn's ambient transaction; a
  row referencing a category is saved after the category; cross-flow actions go through
  `ConversationTurn.HandoffConversation`; inside `FromSql` filter with a boolean flag, never a
  nullable parameter (PostgreSQL `42P18`).

## Status and handoff

All phases (0–10) are done. The phase table, the end-to-end verification notes and the backlog
live in [`docs/HANDOFF.md`](docs/HANDOFF.md), next to the next task: CSV export of a date
range's expenses.

## Commands to verify any change

```bash
dotnet build MyBudget.sln                 # zero warnings
dotnet test MyBudget.sln                  # everything
dotnet format MyBudget.sln --verify-no-changes
```
