# MyBudget-bot — Working Context

Single source of truth for how this repository is built and changed. Keep it in sync when
architecture, scripts, conventions or status change. Details live in
[`docs/TECHNICAL-DESIGN.md`](docs/TECHNICAL-DESIGN.md).

## What this project is

A personal budgeting Telegram bot for a Colombian user: fast expense entry, reliable
reporting. Money is whole COP pesos (`long` / `bigint`), user-facing text is Spanish,
everything technical is English. Deployed as a modular monolith on the `santidev21` VPS
behind `vps-gateway`.

## Non-negotiable rules

1. **Language separation.** Code, identifiers, namespaces, table and column names, config
   keys, log event names, test names, comments, docs and commit messages are **English**.
   Only user-facing strings are Spanish, and they live in resources — never inline in
   business logic, never in the domain or application layers.
2. **Money is exact.** `long` only. Never `float`, `double`, `decimal` persisted, or
   formatted strings. Whole pesos: `35.000 COP` is `35000`.
3. **Expense dates are calendar dates.** `ExpenseDate` is `DateOnly` / `date`, resolved in
   the user's time zone. `CreatedAt` / `UpdatedAt` are UTC instants (`timestamptz`).
   Getting this wrong moves expenses into the wrong month.
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
  MyBudget.Telegram/           # presentation: dispatch, conversations, rendering.
  MyBudget.Api/                # composition root, health, Serilog, --migrate.
tests/                         # Domain, Application, Infrastructure, Telegram, Architecture
docker/app/Dockerfile
docker/postgres/init/          # least-privilege role provisioning (runs once)
scripts/deploy.sh
docs/TECHNICAL-DESIGN.md
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
| Local API port | `http://localhost:8092` (native) or `http://localhost:8091` (Docker) |
| Local database port | `127.0.0.1:5435` (5432/5433/5434 are taken on this host) |
| Fill Telegram settings | `./scripts/init-telegram-env.sh --polling` (never prints secrets) |
| Register the bot in Telegram | `dotnet run --project src/MyBudget.Api -- --configure-telegram` |
| Remove the webhook | `dotnet run --project src/MyBudget.Api -- --delete-webhook` |
| Deploy on VPS | `cd /opt/mybudget && ./scripts/deploy.sh deploy` |

Local chat development: the app reads `.env` in development, so `TELEGRAM_BOT_TOKEN`,
`ALLOWED_TELEGRAM_USER_IDS` and `TELEGRAM_USE_POLLING=true` are enough, then
`dotnet run --project src/MyBudget.Api`. Delete any registered webhook first — Telegram allows
webhooks or polling, never both. Polling reuses the same dispatcher as the webhook, so what
you test locally is what ships.

Addresses: containers `mybudget` / `mybudget-db` / `mybudget-migrator`; networks
`mybudget-net` (external, gateway) and `mybudget-internal-net` (internal, database only).
Database and application ports are never published in production.

## Testing conventions

- Integration tests use **Testcontainers + real PostgreSQL**. Never the EF in-memory
  provider: it cannot enforce CHECK, UNIQUE or FOREIGN KEY constraints, which is exactly
  what those tests verify.
- The shared `DatabaseFixture` migrates once and Respawn resets between tests.
- Raw SQL is used deliberately when a test must produce a state the domain forbids
  (empty names, negative amounts, cross-user references) — those tests prove the database
  refuses it even when application code is bypassed.
- Architecture tests fail the build if the layering is violated.
- Test method names read as sentences; no `Method_Should_Do_Thing` noise.

## Gotchas discovered the hard way

- **Aggregate-created records must have store-generated keys.** EF Core decides whether an
  entity found in a navigation is new or existing by checking whether its key is set. A
  client-assigned GUID makes an added child look like an existing row, and `SaveChanges`
  then fails as a concurrency conflict. `CategoryAlias` and `MonthlyBudgetCategory`
  therefore use `Entity(keyGeneratedByStore: true)` and `ValueGeneratedOnAdd()`.
- **Consequence: an unsaved child has `Guid.Empty` as its key.** Never identify a child by
  its id before it is persisted. `BudgetCategory.RemoveAlias` takes the *instance* for
  exactly this reason; `MonthlyBudget.RemoveAllocation` takes a *category* id, which is
  safe because category keys are client-assigned. `FindAlias(Guid.Empty)` returns null
  rather than matching the first alias.
- **Do not make the `NO ACTION` composite FKs `DEFERRABLE`.** With EF's single-command
  autocommit saves the violation surfaces at implicit commit and EF reports a concurrency
  failure instead of a named foreign key violation.
- **Removing a child from a tracked aggregate relies on EF's orphan deletion**, which is a
  convention derived from the non-nullable foreign key, not something the domain states.
  `OrphanRemovalTests` pins it.
- **`external: false` in `docker-compose.local.yml` is required.** Compose merges network
  definitions, so the base file's `external: true` otherwise survives and local startup
  fails.
- **Both `app` and `migrator` must keep `image: mybudget-app`.** Without the shared image
  name Compose builds the same Dockerfile twice under two tags.
- **The runtime image is Debian, not Alpine**, because `es-CO` formatting needs ICU and
  `TimeZoneInfo` needs tzdata.
- Migration files are marked as generated code in `.editorconfig`; do not reformat them.
- **XML comments must not contain `--`.** It is invalid XML, and it silently broke the
  `.runsettings` coverage file and the API project file. Write `the migration entrypoint`,
  not `the --migrate entrypoint`.
- **Spanish is the neutral resource set**, declared with `NeutralResourcesLanguage` in
  `MyBudget.Application.csproj`. Adding a language means adding
  `Resources/Messages.<culture>.resx` and listing the culture in
  `Localization:SupportedLanguages`. Never name the Spanish file `Messages.es.resx`: the
  neutral file is what removes the satellite-assembly failure mode.
- **Parsing stages are `internal` but tested directly** through `InternalsVisibleTo`. A
  defect in separator handling must be findable without driving the whole pipeline.
- **An inbox row that is `processed` or `ignored` is settled.** Only `failed` reopens a
  claim. Treating `ignored` as claimable would act on an update that was skipped on purpose
  (stale, wrong chat, not allowlisted).
- **`ExecuteDeleteAsync` bypasses the change tracker.** Bulk deletes (inbox purge) run from
  their own scope; reusing a context afterwards trips an identity conflict.
- **Conversation payloads are `jsonb`, so PostgreSQL canonicalises them.** Compare them as
  data, never as strings: `{"step":1}` comes back as `{"step": 1}`.
- **`Options.Create` is ambiguous inside files that import `MyBudget.Telegram.Options`.**
  Qualify it as `Microsoft.Extensions.Options.Options.Create`.
- **`Update.Id` is an `int`** in Telegram.Bot. Widen it to `long` for the inbox, which is a
  bigint.
- **Docker cannot publish a host port for a container whose only network is `internal: true`**
  (it starts with `NetworkSettings.Ports` null and no error). Production publishes nothing and
  keeps the internal network; `docker-compose.local.yml` uses dedicated non-internal networks
  so the database port is reachable for native runs.
- **Configuration binding does not turn a single value into an array.** A `long[]` property
  bound from `"111,222,333"` stays empty, silently. `TelegramOptions.AllowedUserIds` is
  therefore a `string` with explicit parsing and startup validation.
- **A blank configuration value counts as unset.** `appsettings.json` ships
  `ConnectionStrings:Database` as an empty string; treating that as configured stops `.env`
  from ever filling it.
- **Hosted services are singletons.** Both the inbox purge and Telegram polling resolve scoped
  services, so they must create a scope per iteration. This bit twice, and only a test that
  builds the real host catches it: the default smoke test runs with Telegram disabled, so
  `ApiTelegramWiringTests` exists specifically to register the polling path.
- **A tap on the persistent menu outranks the active conversation.** If the router delegated
  first, a label like `📊 Resumen` would be swallowed as flow input and could become a category
  name. `ConversationRouter` matches the menu before handing text to a conversation, and
  starting a menu flow clears whatever was in progress.
- **Callback data is capped at 64 bytes, so identity does not always fit.** The alias screen
  identifies a keyword by its position in a list carried in the conversation payload, then
  removes it by term. A 60-character alias plus a prefix does not fit.
- **`IUserLocalDate` is the only place a UTC instant becomes the user's calendar date.** Budget
  months, and expense dates in Phase 5, go through it. A stored time zone is not trusted: an
  unusable value falls back to UTC instead of raising.
- **A conversation payload that cannot be read is treated as empty.** `jsonb` is canonicalised
  by PostgreSQL and may have been written by an older version of a flow; acting on a
  half-understood draft is worse than starting the step over.

## Status and handoff

**Read this file first, then `docs/TECHNICAL-DESIGN.md`.** A fresh session knows nothing about
this project; everything needed to continue is in the repository, not in anyone's memory.

```
[x] Phase 0  Foundation: solution, build settings, Docker, CI, logging, health
[x] Phase 1  Domain + persistence: entities, schema, constraints, repositories
[x] Phase 2  Money, dates, i18n: parser, formatter, compact input, date parser, catalog
[x] Phase 3  Telegram plumbing: webhook, inbox, allowlist, lock, conversations, onboarding
[x] Phase 4  Categories and monthly budgets          <-- done
[ ] Phase 5  Expenses: guided and compact entry, edit, delete, history   <-- next
[ ] Phase 6  Category matching and keyword learning
[ ] Phase 7  Summary and statistics
[ ] Phase 8  Hardening: verified backups, runbook, rate limiting
[ ] Phase 9  Optional: charts, recurring expenses, CSV export/import
```

**Verified working:** the bot answers `/start`, asks for a time zone, and manages categories and
monthly budgets from a real Telegram account, in polling mode: list, create, rename, change the
icon, activate or deactivate, aliases with the conflict prompt, and set or copy a month's
allocation. 582 tests green, `dotnet build` with zero warnings, `dotnet format` clean.

### Phase 4 delivered — categories and monthly budgets

All exercised through Telegram conversations:

- Category management: create, rename, change the icon, activate and deactivate, and list.
  Deletion is not offered; reusing the name of a deactivated category reactivates it with its
  history intact.
- Alias management: add and remove keywords per category. A term that already belongs to
  another category is a prompt, not a refusal: adding it anyway stores the ambiguity on purpose.
- Monthly budget: the current month is listed, an allocation is set per category, and the
  previous month's budget is copied only when the user asks.
- Past months are immutable in the product. `IBudgetService` returns `PastMonth` rather than
  silently refusing, and the flow renders the reason. The database does not enforce it, so data
  repair stays possible.

Application services: `ICategoryService`, `IBudgetService`, `IUserLocalDate` (the single place a
UTC instant becomes the user's calendar date). Conversation: `CategoriesConversation`, split
across three partial files (core, aliases, budgets).

### Phase 5 scope — expenses core

Next: guided and compact expense entry, the confirmation screen with pending actions, expense
list and detail, edit and delete, and undo. `pending_actions` and the callback-data token
scheme arrive here. Exit criteria: end-to-end flow tests for entry, edit, delete and undo.

### Commands to verify any change

```bash
dotnet build MyBudget.sln                 # zero warnings
dotnet test MyBudget.sln                  # everything
dotnet format MyBudget.sln --verify-no-changes
```

Running it locally: fill `.env` (see `scripts/init-telegram-env.sh`), start the database with
the local overlay, and `dotnet run --project src/MyBudget.Api`. The app reads `.env` in
development, so no variable is repeated on the command line.
