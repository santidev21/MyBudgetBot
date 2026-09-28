# MyBudget-bot

A personal budgeting bot for Telegram: fast expense entry, reliable reporting.

Built as a production-quality .NET 8 modular monolith — clean layering, a real
relational model with database-enforced integrity, deterministic money parsing, and a
deployment that matches the rest of the `vps-gateway` estate.

> **Status: Phase 0–6 complete.** The domain model, PostgreSQL schema, constraints,
> repositories, health checks, structured logging, Docker deployment, the full Telegram
> interface, category and monthly-budget management, guided and compact expense entry, and
> deterministic category matching with keyword learning are in place. 671 tests green.

---

## Product overview

The target user is a Colombian user spending Colombian pesos:

- All amounts are whole COP pesos. `35.000 COP` is stored as `35000`.
- Everything the user sees is **Spanish**.
- Money is displayed with Colombian formatting (`$35.000`, `$1.500.000`).

The design is currency- and locale-aware so more languages and currencies can be added
later as data, not as a rewrite. Today there is exactly one of each.

## Features

**Implemented (Phase 0–6)**

- Domain model: users, categories, aliases, monthly budgets, allocations, expenses.
- PostgreSQL schema with CHECK, UNIQUE and composite FOREIGN KEY constraints.
- Historical monthly budget model that cannot be rewritten retroactively.
- User isolation enforced by the database, not only by application code.
- EF Core migrations applied by a one-shot migrator container under an advisory lock.
- **Colombian money parser**: ~40 accepted input shapes, from `35k` and `35 mil` to
  `$35,000`, non-breaking spaces and `1,5 millones`, with a Spanish message per failure
  reason and no silent rounding.
- **Money formatter** with pinned Colombian separators (not host culture) and comma
  percentages.
- **Compact expense extraction**: `35.000 verduras` becomes an amount plus a description,
  and two equally plausible amounts are reported instead of guessed.
- **Date parser**: `hoy`, `ayer`, `25/09`, `25-09-2026`, `2026-09-25`,
  `25 de septiembre de 2026`.
- **Localization catalog** with the language passed explicitly, a Spanish fallback, and a
  build-failing test if a key is missing.
- **Telegram pipeline**: webhook with dual secret validation (unguessable path plus the
  secret header, both constant-time), an idempotency inbox, an allowlist that fails closed,
  private-chat-only enforcement, stale-update rejection, and a per-user PostgreSQL advisory
  lock so concurrent updates cannot interleave.
- **Conversation state in PostgreSQL**, not in memory: an interrupted flow survives a restart
  or a deploy, and an abandoned one expires instead of being resumed days later.
- **Onboarding, `/start`, `/help`, `/cancel` and a persistent Spanish menu.** Long polling for
  local development, sharing the exact dispatcher the webhook uses.
- Structured JSON logging (Serilog), liveness/readiness/health endpoints.
- Startup configuration validation: a bad time zone or language aborts boot with a clear
  message instead of breaking the first user.
- Least-privilege database roles (`mybudget_migrator` owns the schema; `mybudget_app`
  is DML-only).
- Docker Compose deployment aligned with the `vps-gateway` standard, including the
  versioned gateway site config that restricts health endpoints and the webhook.
- **Category and monthly budget management:** create, rename, change the icon, activate and
  deactivate, add and remove keywords, set an allocation per category, and copy the previous
  month's budget on request. Past months are immutable in the product; deletion is not offered,
  deactivation is, and reusing a deactivated name reactivates it with its history intact.
- **Guided and compact expense entry**, a consume-once confirmation stored server-side, a listing
  with detail, edit and delete, and a global undo on the registration reply.
- **Deterministic category matching** against the user's category names and keywords, with an
  `Ambiguous` outcome that asks instead of guessing, fuzzy matching off by default, and a
  keyword-learning offer that shows exactly what will be stored and warns on a conflict.
- 671 tests: domain units, money and date corpora with property-based tests, localization
  guards, Telegram pipeline and conversation tests, matcher corpus, application contract tests,
  architecture tests, migration guardrails and PostgreSQL integration tests.

**Planned**

| Phase | Scope |
|---|---|
| ~~4~~ | ~~Category and monthly budget management~~ *(done)* |
| ~~5~~ | ~~Expense entry (guided and compact), edit, delete, history~~ *(done)* |
| ~~6~~ | ~~Deterministic category matching and keyword learning~~ *(done)* |
| 7 | Monthly summary and statistics |
| 8 | Backups with verification, runbook, rate limiting |
| 9 | Optional: charts, recurring expenses, CSV export/import |

## Architecture

```
Telegram  ->  HTTPS  ->  vps-gateway (nginx)  ->  mybudget:8080
                                                      |
                                          MyBudget.Api          (host, health, --migrate)
                                          MyBudget.Telegram     (presentation, conversations)
                                          MyBudget.Application  (use cases, policies)
                                          MyBudget.Domain       (entities, invariants, math)
                                          MyBudget.Infrastructure (EF Core, Npgsql, migrations)
                                                      |
                                                 PostgreSQL 16
```

The dependency rule is enforced by tests, not convention:

- `Domain` depends on nothing.
- `Application` depends on `Domain` only — no EF Core, no Telegram types.
- `Telegram` depends on `Application`/`Domain` — never on EF Core or `Infrastructure`.
- `Infrastructure` implements persistence and knows nothing about Telegram.
- `Api` is the composition root.

This is what makes the application layer testable without Telegram and without a
database.

## Technology stack

| Concern | Choice |
|---|---|
| Language / runtime | C# 12, .NET 8 |
| Host | ASP.NET Core minimal API |
| Persistence | EF Core 8 + Npgsql, PostgreSQL 16 |
| Telegram | `Telegram.Bot` (Bot API, webhooks in production) |
| Logging | Serilog (compact JSON in production) |
| Testing | xUnit, FluentAssertions, NSubstitute, Testcontainers, NetArchTest |
| Packaging | Docker, Docker Compose |
| Central package versions | `Directory.Packages.props` |

## Database design

Full detail: [`docs/TECHNICAL-DESIGN.md`](docs/TECHNICAL-DESIGN.md).

```
users ──< categories ──< category_aliases
  │            │
  │            └──────────────< monthly_budget_categories >──── monthly_budgets
  │                                        │                         │
  └──────────────────< expenses ───────────┘                         │
                                                                     └── per user + month
```

Two structural decisions carry the integrity requirements:

1. **Composite foreign keys** — `(category_id, user_id) REFERENCES categories (id, user_id)`.
   The owner is part of every reference, so one user's row can never point at another
   user's data, even if application code is wrong. EF Core cannot express these; they are
   created with raw SQL in the `InitialSchema` migration and covered by integration tests.
2. **`ON DELETE NO ACTION` on category references** — a category that has expenses or a
   funded allocation cannot be deleted. History is structurally indestructible. Erasing a
   user is an explicit, ordered, transactional operation (`IUserDataEraser`).

### Historical monthly budget model

Allocations live in their own per-month table, so a report for September reads
September's allocation row and can never be affected by editing October:

```
monthly_budgets(user, 2026-09) ──< monthly_budget_categories(category=Market, amount=1_000_000)
monthly_budgets(user, 2026-10) ──< monthly_budget_categories(category=Market, amount=1_500_000)
```

The monthly total is always derived (`SUM`), never stored. Model rollover is explicit:
`GetOrCreate` copies nothing, and the bot offers "copy last month's budget" as a
deliberate action.

## Telegram architecture

```
POST /telegram/webhook/{secret}  ->  validate secret header (constant time)
                                 ->  inbox row (idempotency, ON CONFLICT)
                                 ->  allowlist + private chat + stale-update gates
                                 ->  resolve user by telegram_user_id
                                 ->  pg advisory lock per user (serialises concurrent updates)
                                 ->  route: conversation | command | menu
                                 ->  acknowledge, send, mark processed
```

Three decisions carry the robustness:

- **The reply is sent after the state change is committed.** A crash can lose a reply, but it
  can never half-write an expense.
- **Conversation state lives in PostgreSQL.** A deploy mid-flow does not strand the user, and
  an abandoned conversation expires rather than being resumed days later.
- **Nothing is trusted from callback data.** Buttons carry short tokens, and the state they
  refer to is re-loaded and ownership-checked before anything happens.

Registering the bot is an explicit step, never something that happens at startup:

```bash
dotnet run --project src/MyBudget.Api -- --configure-telegram
```

That sets the command menu (Spanish, via Telegram's own `language_code`) and the webhook.
`--delete-webhook` removes it, which is required before using `TELEGRAM_USE_POLLING=true`.

## Money handling

- Exact integers only. `long` in the domain, `bigint` in PostgreSQL. Never `float`/`double`.
- No formatted strings are ever stored.
- `IMoneyParser` accepts everything a Colombian user actually types: `35000`, `35.000`,
  `35,000`, `35 000`, `$35.000`, `35000 pesos`, `35k`, `35 mil`, `1,5 millones`, and the
  non-breaking spaces mobile keyboards send. ~40 shapes are covered by tests.
- It never throws and never guesses: every input yields a value, an explicit request for
  clarification, or a named reason (`Empty`, `NotANumber`, `Negative`, `NonPositive`,
  `FractionNotAllowed`, `TooLarge`, `MalformedGrouping`) that maps to a Spanish message.
- `38.500,25` is rejected rather than rounded: COP has no cents, and silently changing
  someone's money is not acceptable.
- `IMoneyFormatter` pins Colombian separators explicitly instead of relying on host ICU
  data: `3500 → $3.500`, `1500000 → $1.500.000`, `60,6 %`.
- A second currency is a `CurrencyDefinition` entry: decimal places and separators drive the
  parser, so no parsing code changes. A currency with decimal places is the only case that
  can produce a genuine ambiguity, and it is handled by asking.
- Covered by a data-driven corpus plus FsCheck properties: formatting then parsing always
  returns the same amount, and parsing arbitrary text never throws.

## Localization strategy

User-facing Spanish text lives in resources, never inline in business logic. The message
catalog takes the language as an explicit parameter rather than reading ambient culture,
because a Telegram bot has no HTTP request culture to rely on. Tests fail the build if a key
is missing, if a value is blank, or if the declared keys and the resources disagree.

All identifiers — types, methods, tables, columns, config keys, log events, tests,
commits, documentation — are in English. Only resource *values* are Spanish.

## Local development

Requirements: .NET 8 SDK, Docker.

```bash
# 1. Configuration. Generates the two webhook secrets and reports what only you can provide.
cp .env.example .env
./scripts/init-telegram-env.sh --polling

# 2. Database only (loopback on 127.0.0.1:5435)
docker compose -f docker-compose.yml -f docker-compose.local.yml up -d db

# 3. Apply migrations. Reads .env, so no connection string on the command line.
dotnet dotnet-ef database update \
  --project src/MyBudget.Infrastructure \
  --startup-project src/MyBudget.Infrastructure

# 4. Run the API (http://localhost:8092). Also reads .env.
dotnet run --project src/MyBudget.Api

# Or run the whole stack in Docker (app on 127.0.0.1:8091, polling enabled)
docker compose -f docker-compose.yml -f docker-compose.local.yml up --build
```

In development the API reads `.env`, the same file Docker Compose uses, so a value is
configured once. An explicitly set environment variable always wins over the file.

Port 5435 is deliberate: 5432 is the PostgreSQL default that every tool tries to grab, and
other projects on this host already use 5433 and 5434.

Health endpoints:

| Endpoint | Purpose | Exposure |
|---|---|---|
| `GET /health/live` | liveness, no dependencies | public |
| `GET /health/ready` | PostgreSQL connectivity | private ranges only |
| `GET /health` | full report with timings | private ranges only |

The production restriction is not left to the server: it is versioned in
[`docs/gateway/mybudget.santidev21.tech.conf`](docs/gateway/mybudget.santidev21.tech.conf),
which is copied into the gateway's `sites-enabled/` during deployment.

### Tests

```bash
dotnet test MyBudget.sln                          # everything
dotnet test tests/MyBudget.Domain.Tests           # no Docker needed
```

Integration tests start a real PostgreSQL container (Testcontainers). The EF in-memory
provider is deliberately **not** used: it cannot enforce CHECK, UNIQUE or FOREIGN KEY
constraints, which are precisely what the integration tests exist to verify.

### Migrations

```bash
dotnet dotnet-ef migrations add <Name> \
  --project src/MyBudget.Infrastructure \
  --startup-project src/MyBudget.Infrastructure
```

Never edit an applied migration. The schema is changed only through migrations.

## Environment variables

See [`.env.example`](.env.example) for the full annotated list.

| Variable | Purpose |
|---|---|
| `TELEGRAM_BOT_TOKEN` | Bot token from BotFather |
| `TELEGRAM_WEBHOOK_SECRET` | Shared secret validated on every webhook call |
| `TELEGRAM_WEBHOOK_PATH` | Random URL segment making the webhook unguessable |
| `ALLOWED_TELEGRAM_USER_IDS` | Allowlist; unknown users are refused |
| `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD` | Instance and database |
| `DB_APP_USER`, `DB_APP_PASSWORD` | Runtime role, DML only |
| `DB_MIGRATOR_USER`, `DB_MIGRATOR_PASSWORD` | Migration role, owns the schema |
| `DEFAULT_LANGUAGE`, `DEFAULT_TIME_ZONE` | `es`, `America/Bogota` |

Secrets are never committed. `.env` is excluded by both `.gitignore` and `.dockerignore`.

## Docker deployment

Production topology follows the `vps-gateway` standard:

- `mybudget-net` — external, shared with the gateway (ingress and egress to Telegram).
- `mybudget-internal-net` — internal, database only, never shared with the gateway.
- Containers: `mybudget-db`, `mybudget-migrator` (one-shot), `mybudget`.
- No ports are published to the host; only the gateway is public.

First deployment on the VPS:

```bash
git clone <repo> /opt/mybudget && cd /opt/mybudget
cp .env.example .env    # fill in real secrets
chmod +x scripts/deploy.sh
./scripts/deploy.sh deploy
```

The gateway must then be pointed at `mybudget:8080` for
`mybudget.santidev21.tech` by copying
[`docs/gateway/mybudget.santidev21.tech.conf`](docs/gateway/mybudget.santidev21.tech.conf)
into `vps-gateway/sites-enabled/`, following `vps-gateway/docs/STANDARD.md`. Subsequent
deployments happen automatically on push to `main`.

## Backup strategy

**Docker persistence is not a backup.** The strategy (implemented in Phase 8) is:

- Nightly `pg_dump --format=custom` into `mybudget_pg_backups`, a volume separate from the
  data volume.
- Encrypted off-site copies to independent storage.
- Retention: 7 daily, 4 weekly, 12 monthly.
- **Verification**: every dump is listed with `pg_restore --list`; weekly, the newest dump
  is restored into a throwaway database and sanity-checked. A backup that has never been
  restored is not a backup.
- Documented restore procedure with a stated RPO (≤ 24h) and RTO (≤ 30 min).

## Roadmap

Recurring expenses, income tracking, savings goals, CSV export/import, a web dashboard and
scheduled Telegram summaries. None of these are required for the product to be useful, and none
are implemented prematurely. Categorization stays deterministic; there is no AI in that path.
The schema and interfaces do not block any of the rest.

## License

Private project. All rights reserved.
