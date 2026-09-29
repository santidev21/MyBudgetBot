# MyBudget-bot

![.NET](https://img.shields.io/badge/.NET-8-purple)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-blue)
![Telegram](https://img.shields.io/badge/Telegram-Bot-26A5E4)
![License](https://img.shields.io/badge/license-private-lightgrey)

**MyBudget-bot** is a personal budgeting bot for Telegram. It makes expense entry fast — type
`35.000 verduras` and it is registered — and reporting reliable, because money is exact, dates
are calendar dates in the user's time zone, and history cannot be rewritten.

Built as a production-quality .NET 8 modular monolith: clean layering enforced by tests, a
relational model with database-enforced integrity, a deterministic Colombian money parser, and a
deployment that matches the rest of the `vps-gateway` estate.

<!-- Hero screenshot:
![Monthly summary](docs/images/summary.png)
-->

---

## Features

**Fast, exact expense entry**

- Guided entry (amount → description → category → confirmation) and compact entry from free
  text (`35.000 verduras`).
- Colombian money parser covering ~40 input shapes: `35k`, `35 mil`, `$35,000`, non-breaking
  spaces, `1,5 millones`. No silent rounding, a Spanish message per failure reason.
- Dates as calendar dates in the user's time zone (`hoy`, `ayer`, `25/09`, `25 de septiembre de
  2026`); future dates are rejected.
- Edit, delete, and a global undo on the registration reply.

**Categories and monthly budgets**

- Create, rename, change the icon, activate/deactivate, and manage keywords (aliases).
- Per-month allocations with an explicit "copy last month's budget" action; past months are
  immutable in the product.
- Deterministic category matcher: ambiguity asks instead of guessing, fuzzy matching is off by
  default, and a learned keyword is shown before it is stored.
- History is never deleted: a category with expenses is deactivated, never removed.

**Reporting**

- Monthly summary against the month's historical allocation, with text bars and month navigation.
- Date-range history with keyset pagination and day grouping.
- Statistics: share per category, daily spending, largest expenses, average daily, and a period
  comparison that warns when a period is still in progress.
- Hand-drawn PNG charts (by category and by day) with no native dependency.

**Automation**

- Recurring monthly rules applied by a scheduler, dated in the user's own calendar.
- Budget alerts at 80 % and 100 %, once per threshold and month.
- A monthly closing sent on the user's first local day, with a button to copy the budget forward.
  It is claimed once per user and month, so a restart never repeats it.

**Reliability and Telegram plumbing**

- Webhook with dual secret validation (unguessable path + secret header, both constant-time),
  an idempotency inbox, an allowlist that fails closed, private-chat-only, stale-update
  rejection, and a per-user PostgreSQL advisory lock.
- Conversation state lives in PostgreSQL: a deploy mid-flow does not strand the user, and an
  abandoned flow expires instead of being resumed days later.
- Localization catalog with the language passed explicitly, a Spanish fallback, and a test that
  fails the build if a key is missing or blank.
- Structured JSON logging, liveness/readiness/health endpoints, and startup configuration
  validation.
- **801 tests**: domain units, money and date corpora with property tests, localization guards,
  Telegram pipeline and conversation tests, matcher corpus, architecture tests, migration
  guardrails, and PostgreSQL integration tests.

---

## Tech stack

| Layer | Technology |
|---|---|
| Runtime | C# 12, .NET 8 |
| Host | ASP.NET Core minimal API |
| Persistence | EF Core 8 + Npgsql, PostgreSQL 16 |
| Telegram | `Telegram.Bot` (webhooks in production, long polling locally) |
| Logging | Serilog (compact JSON in production) |
| Testing | xUnit, FluentAssertions, NSubstitute, Testcontainers, NetArchTest, FsCheck |
| Packaging | Docker, Docker Compose |
| CI/CD | GitHub Actions (test → format → build → Trivy scan → deploy) |

---

## Architecture

Served at `https://mybudget.santidev21.tech/` behind the `vps-gateway` reverse proxy:

```
Telegram ──HTTPS──▶ vps-gateway (nginx :443)
                        │ proxy_pass http://mybudget:8080
                        ▼
                   mybudget          (mybudget-net + mybudget-internal-net)
                        │
                   mybudget-db       (mybudget-internal-net only)
```

**Docker services:**

| Service | Description |
|---|---|
| `mybudget-db` | PostgreSQL 16 |
| `mybudget-migrator` | Applies EF Core migrations then exits (one-shot) |
| `mybudget` | .NET 8 app: webhook, schedulers, health (internal, no published ports) |

- `mybudget-net` (external, shared with the gateway): `mybudget`.
- `mybudget-internal-net` (internal): `mybudget` + `mybudget-db`. The database is **never** on
  the shared network.
- The application never migrates at startup: a failed migration must be loud, not a half-booted
  service.

**Clean Architecture** (enforced by architecture tests, not convention):

- `MyBudget.Domain` → entities, value objects, invariants, pure math. No dependencies.
- `MyBudget.Application` → use cases and persistence abstractions. Domain only; no EF Core, no
  Telegram types.
- `MyBudget.Infrastructure` → EF Core, Npgsql, migrations, repositories.
- `MyBudget.Telegram` → presentation: dispatch, conversations, charts, rendering.
- `MyBudget.Api` → composition root, health, Serilog, `--migrate`.

### Data integrity

- **Composite foreign keys** — `(category_id, user_id) REFERENCES categories (id, user_id)`.
  The owner is part of every reference, so one user's row can never point at another user's data
  even if application code is wrong. EF Core cannot express these, so they are created with raw
  SQL in the `InitialSchema` migration and asserted by integration tests.
- **`ON DELETE NO ACTION` on category references** — a category with expenses or a funded
  allocation cannot be deleted; history is structurally indestructible. Erasing a user is an
  explicit, ordered, transactional operation (`IUserDataEraser`).
- **Historical monthly budgets** — allocations live in their own per-month table, so editing
  October can never change September's report. Totals are always derived with `SUM`, never stored.

---

## Project Structure

```text
MyBudget-bot/
├─ MyBudget.sln
├─ Directory.Build.props        # shared build settings
├─ Directory.Packages.props     # central package versions (single source of truth)
├─ src/
│  ├─ MyBudget.Domain/          # entities, value objects, pure math
│  ├─ MyBudget.Application/     # use cases + persistence abstractions
│  ├─ MyBudget.Infrastructure/  # EF Core, Npgsql, migrations, repositories
│  ├─ MyBudget.Telegram/        # dispatch, conversations, charts, rendering
│  └─ MyBudget.Api/             # composition root, health, Serilog, --migrate
├─ tests/                       # Domain, Application, Infrastructure, Telegram, Architecture, Api
├─ docker/app/Dockerfile
├─ docker/postgres/init/        # least-privilege role provisioning (runs once)
├─ scripts/                     # deploy, backup, verify-backup, restore, cron install
├─ docs/                        # technical design, backups, deployment, handoff, gateway config
└─ .github/                     # CI/CD workflows
```

---

## Local Development

Requirements: .NET 8 SDK and Docker.

### 1. Environment setup

```bash
cp .env.example .env
./scripts/init-telegram-env.sh --polling   # generates the webhook secrets, never prints them
```

Fill in what only you can provide: `TELEGRAM_BOT_TOKEN` (from @BotFather) and
`ALLOWED_TELEGRAM_USER_IDS`. For local chat development, `TELEGRAM_USE_POLLING=true` plus those
two values are enough. Delete any registered webhook first — Telegram allows webhooks or
polling, never both.

### 2. Database (loopback only)

PostgreSQL 16 always runs in Docker on `127.0.0.1:5435` and is never exposed externally. Port
5435 is deliberate: 5432 is the default every tool grabs, and other projects on this host use
5433 and 5434.

```bash
docker compose -f docker-compose.yml -f docker-compose.local.yml up -d db
```

### 3. Apply migrations

```bash
dotnet dotnet-ef database update \
  --project src/MyBudget.Infrastructure \
  --startup-project src/MyBudget.Infrastructure
```

The local connection string uses the **migrator** role: `dotnet ef` needs DDL, and the runtime
role intentionally has none. The app itself always runs as `mybudget_app`.

### 4. Run

```bash
# API natively (http://localhost:8092), reading the same .env Docker Compose uses
dotnet run --project src/MyBudget.Api

# Or the whole stack in Docker, closest to prod (app on 127.0.0.1:8091)
docker compose -f docker-compose.yml -f docker-compose.local.yml up --build
```

### 5. Register the bot

```bash
dotnet run --project src/MyBudget.Api -- --configure-telegram   # webhook + command menu
dotnet run --project src/MyBudget.Api -- --delete-webhook       # required before polling
```

### Commands

| Command | Purpose |
|---|---|
| `dotnet build MyBudget.sln` | Build (zero warnings) |
| `dotnet test MyBudget.sln` | All tests (Docker required for integration) |
| `dotnet test tests/MyBudget.Domain.Tests` | Unit tests only, no Docker |
| `dotnet format MyBudget.sln --verify-no-changes` | Format check |
| `docker compose -f docker-compose.yml -f docker-compose.local.yml up --build` | Full stack in Docker |
| `dotnet run --project src/MyBudget.Api` | API natively on `:8092` |
| `./scripts/deploy.sh deploy` | Validated deploy on the VPS |

### Migrations

```bash
# New migration: never edit an applied one.
dotnet dotnet-ef migrations add <Name> \
  --project src/MyBudget.Infrastructure \
  --startup-project src/MyBudget.Infrastructure
```

Integration tests start a real PostgreSQL container (Testcontainers). The EF in-memory provider
is deliberately **not** used: it cannot enforce CHECK, UNIQUE or FOREIGN KEY constraints, which
is exactly what those tests verify.

### Health endpoints

| Endpoint | Purpose | Exposure |
|---|---|---|
| `GET /health/live` | Liveness, no dependencies | Public |
| `GET /health/ready` | PostgreSQL connectivity | Private ranges only |
| `GET /health` | Full report with timings | Private ranges only |

The restriction is versioned in
[`docs/gateway/mybudget.santidev21.tech.conf`](docs/gateway/mybudget.santidev21.tech.conf) and
copied into the gateway's `sites-enabled/` during deployment.

---

## Deployment

Deploys happen automatically on push to `main` via GitHub Actions: test → format → Docker build
→ Trivy scan → SSH to the VPS → `./scripts/deploy.sh deploy`. The deploy backs up the database,
verifies the dump, pulls a clean checkout, waits for the one-shot migrator, and checks readiness;
it rolls back automatically if readiness never turns healthy. For VPS setup and manual commands,
see [`docs/DEPLOYMENT.md`](docs/DEPLOYMENT.md).

---

## Screenshots

<!-- Telegram screenshots go here:
![Monthly summary](docs/images/summary.png)
![Spending chart](docs/images/chart.png)
-->

The bot answers in Spanish. A sample of what it sends:

```text
📅 Cierre de agosto 2026

Total gastado: $1.500.000
Gastos registrados: 12
El período ya está completo; estas cifras no cambian.

Comparación con julio 2026:
▲ 25 % más que julio 2026.

Categorías con más gasto:
🍔 Comida: $700.000 ▓▓▓▓▓▓▓▓▓▓ 46,7 %
🎬 Ocio: $200.000 ▓▓▓░░░░░░░ 13,3 %

🚨 Te sobregiraste: gastaste $1.500.000 de $1.000.000 asignados.
```

---

## Security

- Allowlist (`ALLOWED_TELEGRAM_USER_IDS`) that fails closed, private chats only; unknown users
  are refused in Spanish and logged by id only.
- Webhook path carries a random segment and requires the secret header, compared in constant
  time; replay is absorbed by the idempotency inbox.
- Every scoped repository query takes `userId` first, and composite foreign keys make cross-user
  references impossible in PostgreSQL even if application code is wrong.
- Least-privilege database roles: `mybudget_migrator` owns the schema (DDL); `mybudget_app` is
  DML-only. The database is on an internal network with no published ports.
- Secrets live only in `.env` or the environment; Serilog redacts bot tokens, passwords,
  connection strings and `Authorization`.
- Per-user inbound throttling and an outbound Telegram 429 retry policy that honours
  `retry_after`.
- Verified backups: nightly dumps with `pg_restore --list`, a weekly restore into a throwaway
  database with sanity checks, and an admin alert on failure. RPO ≤ 24 h, RTO ≤ 30 min. See
  [`docs/BACKUPS.md`](docs/BACKUPS.md).

---

## AI Context

- [`AGENTS.md`](AGENTS.md) — project snapshot (stack, layout, commands, working rules, gotchas)
- [`docs/TECHNICAL-DESIGN.md`](docs/TECHNICAL-DESIGN.md) — architecture, schema, UX flows, testing
- [`docs/DEPLOYMENT.md`](docs/DEPLOYMENT.md) — deploy, rollback and operations
- [`docs/BACKUPS.md`](docs/BACKUPS.md) — backup, verification and restore runbook
- [`docs/HANDOFF.md`](docs/HANDOFF.md) — next task and backlog

---

## To Do

- [ ] **Export CSV** of a date range's expenses. Import waits for later because of data integrity.
- [ ] **Onboarding with seed data.** On first use, offer the default categories (Mensualidades,
  Ocio, Ropa, Otras, Regalos, Vivienda, Mercado, Deporte) and leave one example recurring rule
  ready. Skippable: it never forces the user.
- [ ] **Edit a recurring rule** (amount and day) instead of deleting and recreating it.
- [ ] **Configure encrypted off-site backups** (`BACKUP_OFFSITE_TARGET` + `BACKUP_AGE_RECIPIENT`).
  Local backups are verified, but a copy that only lives on the VPS is lost with the VPS.

Out of scope by design: multi-currency, income/accounts/payments, CSV import, point-in-time
recovery, hard-deleting history, and AI-based categorization.

---

## License

Private project. All rights reserved.
