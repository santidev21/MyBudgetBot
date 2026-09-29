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
docs/HANDOFF.md                # next task prompt and backlog
```

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

**EF Core and keys**

- Aggregate-created records need store-generated keys (`Entity(keyGeneratedByStore: true)` +
  `ValueGeneratedOnAdd()`): EF treats a client-assigned key on a child found in a navigation as
  an existing row and fails as a concurrency conflict. Such an unsaved child has `Guid.Empty`,
  so never identify it by id (`RemoveAlias` takes the instance).
- Removing a child from a tracked aggregate relies on EF orphan deletion (the non-nullable FK
  convention); `OrphanRemovalTests` pins it.
- Do not make the `NO ACTION` composite FKs `DEFERRABLE`: EF's autocommit save would report a
  concurrency failure instead of a named foreign key violation.
- EF cannot order an insert by a composite FK it does not model, so an operational row
  referencing a category (`budget_alerts`) must be written after the category exists.
- `ExecuteDeleteAsync` bypasses the change tracker; bulk deletes run from their own scope.

**Configuration and hosting**

- Configuration binding does not turn a single value into an array: `AllowedUserIds` is a
  `string` with explicit parsing. A blank value counts as unset, so `.env` can fill it.
- Hosted services are singletons; resolve scoped services from a scope per iteration. Only a
  test that builds the real host catches a mistake (`ApiTelegramWiringTests`).
- `Options.Create` is ambiguous inside files importing `MyBudget.Telegram.Options`; qualify it.
- The runtime image is Debian for ICU and tzdata; `external: false` in the local compose overlay
  is required; both `app` and `migrator` keep `image: mybudget-app`.
- Docker cannot publish a host port for a container whose only network is internal.

**Text, resources and payloads**

- Spanish is the neutral resource set; never name it `Messages.es.resx`, and add every key to
  `MessageKeys` or the catalog test fails the build.
- XML comments must not contain `--`.
- Conversation payloads are `jsonb`: compare as data, never strings. A payload that cannot be
  read is treated as empty. A computed payload property must be `[JsonIgnore]`.
- Callback data is capped at 64 bytes; identify a row by position or term when the id does not
  fit. A menu tap outranks the active conversation, and some callbacks (Undo) arrive after the
  flow is gone, handled as global callbacks.
- A confirmation is claimed once with a conditional `UPDATE`, never re-read; the callback
  carries only the pending id. An empty listing must still carry the notices it was built with.

**Dates, money and reporting**

- `IUserLocalDate` is the only UTC-to-local conversion; an unusable stored zone falls back to
  UTC. `ExpenseDate` is a `DateOnly`; report sums are derived in SQL, never stored.
- The matcher's 0.20 partial-overlap floor and epsilon comparison are load-bearing; signals are
  additive per query/keyword and the category takes its best term.
- A budget-alert marker is recorded whether or not the notification is delivered: the marker
  stops the bot repeating itself.

**Operations**

- The EF migration history table is mixed case: quote `"__EFMigrationsHistory"` in raw SQL.
- `pg_restore` exits 0 unless `--exit-on-error`; the drill must actually restore, not just list.
- Pre-deploy and nightly dumps share the backup volume.

**Recurring and charts**

- A recurring rule is configuration: `last_generated_date` and the generated expense commit in
  the same transaction, which is the whole idempotency argument. Month-end clamps to the last
  day. The scheduler is registered only with a bot token, first pass delayed one minute.
- The monthly closing is claimed, not read: `monthly_closings` is inserted with `ON CONFLICT DO
  NOTHING` on `(user_id, year, month)`, before the message, so a restart cannot repeat it.
- The closing fires on the last local day from 23:59 and falls back to the first local day; both
  resolve to the same `closedPeriod`, which is what keeps the marker exactly-once. The
  copy-budget button was removed when budgets became recurring.
- A closing with no spending and no allocation is skipped and its marker stays unspent: a
  notification feature that talks about nothing is a notification feature that gets muted.
- The daily reminder and the closing share `ScheduledNotificationsScheduler` (every minute,
  only with a bot token). The reminder is skipped before the user's local 21:00 and when an
  expense already exists that local day; it claims `("daily", local day)` in
  `reminder_deliveries` before sending, so frequent ticks are safe.
- Adding a menu section touches `MainMenu.ActionKeys`, the keyboard rows, the menu test and the
  router mapping.
- Charts are hand-drawn (`RgbCanvas` + 5x7 bitmap font + PNG over `ZLibStream`) to avoid native
  dependencies; unknown glyphs render blank, so names stay in the caption, and the text screens
  always carry the exact numbers.

**Settings, budgets and routing**

- Erasing the user runs inside the turn's ambient transaction: `UserWorkLock` already opens one,
  so `IUserDataEraser` must reuse `Database.CurrentTransaction` instead of beginning a second.
- `ConversationTurn.UserRemoved` tells the dispatcher to settle the inbox with a `null` owner;
  otherwise `CompleteAsync` would write a `user_id` that no longer exists and hit the FK.
- The recurring budget is a fallback, not a copy: `budget_defaults.effective_from` stops a default
  from appearing in months before it existed, and a month's own row always wins.
- A row referencing a category must be saved after the category exists in the same context: EF
  does not model the composite `(category_id, user_id)` foreign key, so `budget_defaults` (like
  `budget_alerts`) needs its own `SaveChanges`.
- Cross-flow actions use `ConversationTurn.HandoffConversation` + `IHandoffConversation`
  (`ResolveHandoffAsync`); that is how the category breakdown opens an expense in the expenses
  flow without duplicating edit/delete.
- A nullable parameter inside `FromSql` fails with PostgreSQL `42P18`; filter with a boolean flag
  (`({hasCategory} = FALSE OR category_id = {category})`) like the keyset cursor does.

## Status and handoff

| Phase | What | State |
|---|---|---|
| 0–1 | Foundation; domain + schema, constraints, repositories | done |
| 2 | Money, dates, i18n: parsers, formatter, catalog | done |
| 3 | Telegram plumbing: webhook, inbox, allowlist, conversations, onboarding | done |
| 4 | Categories and monthly budgets | done |
| 5 | Expenses: guided and compact entry, edit, delete, history | done |
| 6 | Category matching and keyword learning | done |
| 7 | Summary, range history and statistics | done |
| 8 | Hardening: verified backups, runbook, rate limits, deploy | done |
| 9 | Recurring expenses, spending charts, budget alerts, scheduled summaries | done |
| 10 | Settings (data erasure, reminder switch), recurring budget, per-category breakdown, numbered category chart, daily reminder, 23:59 monthly closing | done |

Verified end to end through Telegram: categories and budgets; guided and compact expenses with
suggestion, keyword learning, edit, delete and undo; recurring rules applied by a scheduler; 80 %
and 100 % budget alerts; month summary, range history, statistics and category/daily charts;
the closing of the last month sent at 23:59 on the user's last local day, with the first local
day as fallback; verified nightly backups;
reproducible deploy. Settings erases everything and switches the daily reminder; a budget set
once recurs every month with per-month overrides; the summary lists remaining budget and drills
into each category's movements. **828 tests green**, build with zero warnings, `dotnet format`
clean.

Next: CSV export of a date range's expenses, then seed categories and recurring-rule editing.
The backlog and decisions are in [`docs/HANDOFF.md`](docs/HANDOFF.md).

## Commands to verify any change

```bash
dotnet build MyBudget.sln                 # zero warnings
dotnet test MyBudget.sln                  # everything
dotnet format MyBudget.sln --verify-no-changes
```
