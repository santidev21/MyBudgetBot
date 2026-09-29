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
scripts/backup.sh              # nightly verified backup
scripts/verify-backup.sh       # restore drill into a throwaway database
scripts/restore.sh             # break-glass restore into the live database
scripts/install-backup-cron.sh # installs the nightly and weekly cron entries
docs/TECHNICAL-DESIGN.md
docs/BACKUPS.md                # backup, verification and restore runbook
docs/DEPLOYMENT.md             # deploy, rollback and operations
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
| Backup now | `cd /opt/mybudget && ./scripts/backup.sh` |
| Restore drill | `cd /opt/mybudget && ./scripts/verify-backup.sh` |
| Install backup cron | `cd /opt/mybudget && ./scripts/install-backup-cron.sh` |

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
- **A confirmation is claimed, never re-read.** The draft lives in `pending_actions` and is
  consumed with one conditional `UPDATE` (`consumed_at IS NULL`). The callback carries only the
  pending id, so a replayed or forged callback reads nothing and acts once.
- **Some callbacks arrive after the flow is gone.** `↩️ Deshacer` sits on the message that
  follows a completed registration. `ConversationRouter` therefore runs `IGlobalCallback`
  handlers when no conversation is active, before declaring the callback expired.
- **An empty listing must still carry the notices it was given.** The month list reported
  "nothing to show" and dropped the "deleted" notice when a delete emptied the month. Any
  builder that short-circuits has to keep its prefix responses.
- **The matcher's 0.20 partial-overlap floor is load-bearing.** It is what lets a lone fuzzy match
  (0.20 + 0.45) reach the 0.65 threshold; without it, fuzzy would never fire and the signal table
  would quietly become dead code. Scores are compared with a small epsilon because that threshold
  is the sum of two binary fractions.
- **Signals are additive per query/keyword pair, then the category takes its best term.** The
  category-name bonus is applied to the name term before that maximum, so a name that ties an
  alias wins by 0.05. Summing across a category's aliases would let a category with many keywords
  buy a score it did not earn.
- **A `None` result is the only path that carries a learn term.** The unrecognized description
  travels in the conversation payload and the offer is shown only after the user picks a category,
  so the flow never asks to save a term to a category that is not chosen yet. The conflict prompt
  reuses the alias screen's wording rather than a second copy.
- **A computed property on a conversation payload must be `[JsonIgnore]`.** `ReportingPayload.Period`
  returns a `MonthPeriod`, whose `Previous`/`Next` recurse; without the attribute `System.Text.Json`
  walks into it and the payload write fails. Computed payload properties are for reading, never
  serialized.
- **Report sums are derived in SQL, never stored.** `IExpenseReadRepository` groups and sums in
  PostgreSQL; `IReportService` only merges the totals with category labels. The keyset page query
  is raw SQL because the tiebreaker is a `uuid`, which C# cannot compare with an operator.
- **The EF migration history table is mixed case and must be quoted.** EF creates
  `"__EFMigrationsHistory"`; unquoted `__EFMigrationsHistory` folds to lowercase and the restore
  drill fails with "relation does not exist" on a perfectly good dump. Quote it in raw SQL.
- **`pg_restore` exits 0 even when it skipped objects.** Only `--exit-on-error` makes a failed
  restore loud; without it a missing relation surfaces later as a confusing sanity failure.
- **The restore drill must not trust a dump it has only listed.** `pg_restore --list` proves the
  file is readable, not that it loads. `verify-backup.sh` actually restores it and checks the
  data, which is the only step that turns a dump into a backup.
- **Pre-deploy and nightly dumps live in the same volume.** `deploy.sh` uses the `backup-lib`
  primitives so the pre-deploy dump is verified and pruned like any other, instead of sitting on
  a host path that off-site copies and the drill would never see.
- **A recurring rule is configuration, never history.** Deleting a rule leaves its expenses
  alone. `last_generated_date` and the generated expense commit in the same transaction, and
  that is the whole idempotency argument: a second pass cannot duplicate a month.
- **Month-end clamping is deliberate.** A rule due on the 31st falls on the 28th or 29th in
  February. The rule stores a day of month plus a `DateOnly` start, never a "next due" instant.
- **The recurring scheduler is registered only when a bot token is configured.** Recording an
  expense the user is never told about would be worse than waiting for the next start. Its first
  pass is delayed one minute so host startup never blocks on the database.
- **Adding a menu section touches more than the label.** `MainMenu.ActionKeys`, the reply
  keyboard rows and the test that pins seven items in four rows all move together; the router
  then needs the new key mapped or the tap answers "not ready yet".
- **`ApplyDueAsync` loads categories per user to label the notification at read time.** The
  label is not stored on the rule or the expense: renaming a category still updates how old
  recurring expenses are reported, like every other report.

## Status and handoff

**Read this file first, then `docs/TECHNICAL-DESIGN.md`.** A fresh session knows nothing about
this project; everything needed to continue is in the repository, not in anyone's memory.

```
[x] Phase 0  Foundation: solution, build settings, Docker, CI, logging, health
[x] Phase 1  Domain + persistence: entities, schema, constraints, repositories
[x] Phase 2  Money, dates, i18n: parser, formatter, compact input, date parser, catalog
[x] Phase 3  Telegram plumbing: webhook, inbox, allowlist, lock, conversations, onboarding
[x] Phase 4  Categories and monthly budgets          <-- done
[x] Phase 5  Expenses: guided and compact entry, edit, delete, history   <-- done
[x] Phase 6  Category matching and keyword learning   <-- done
[x] Phase 7  Summary and statistics                   <-- done
[x] Phase 8  Hardening: verified backups, runbook, rate limiting   <-- done
[x] Phase 9  Recurring expenses (the chosen optional piece)   <-- done
[ ] Phase 9+ Optional: charts, CSV export/import, scheduled summaries   <-- only with a real need
```

**Verified working:** the bot answers `/start`, asks for a time zone, and from a real Telegram
account (in production over the webhook, in development over polling) it manages categories and
monthly budgets, records expenses both guided and compact, suggests the category from the
description, lets the user teach it a keyword, edits and deletes expenses, undoes a
registration, manages monthly recurring rules that the scheduler applies on their due date, and
reports the month (`📊 Resumen`), the range history (`📋 Gastos`) and the month's statistics
(`📈 Estadísticas`). The nightly backup is verified by a real restore drill and the deploy
refuses to run without a verified dump and a clean checkout. 750 tests green, `dotnet build`
with zero warnings, `dotnet format` clean.

### Phase 8 delivered — hardening

- **Verified backups.** `scripts/backup.sh` dumps into the `mybudget_pg_backups` volume (separate
  from the data volume), proves the dump is readable with `pg_restore --list`, promotes it into
  ISO-week and calendar-month buckets by hardlink, optionally writes an `age`-encrypted off-site
  copy, and prunes to 7 daily / 4 weekly / 12 monthly. `scripts/verify-backup.sh` is the restore
  drill: throwaway `mybudget_restore_check` database, `pg_restore --exit-on-error`, sanity SQL
  (migration history present, no non-positive amount, no missing calendar date, no user without a
  Telegram id), then drop. Failure alerts the admin through the Telegram API directly, so an alert
  works even when the application is down. `scripts/restore.sh` is the break-glass restore into
  the live database. `scripts/install-backup-cron.sh` installs the schedule idempotently.
  Runbook in `docs/BACKUPS.md`; a drill against the local stack is recorded there.
- **Inbound throttle.** `SlidingWindowUserRateLimiter` gives each Telegram user a one-minute
  sliding budget (`Telegram:UserRateLimitPerMinute`, default 30). The gate runs after the stale
  check, so replayed updates cannot spend a current message's allowance, and before the user
  lookup, so refusing is cheap. One `RateLimited` notice per window; the rest are dropped
  silently. The notice is a `Messages.resx` key like every other user-facing string.
- **429 hardening.** `TelegramRetryPolicy` honours Telegram's `retry_after`, falls back to one
  second when it is missing or non-positive, and caps it at 60 seconds so an absurd value cannot
  park the pipeline.
- **Reproducible deploy.** `deploy.sh` backs up and verifies before it builds, and `pull` now
  does `git reset --hard origin/main` plus `git clean -fd` and asserts a clean working tree, so
  the build context is exactly the commit. The pre-deploy dump lives in the same backup volume.

### Phase 9 delivered — recurring expenses

The chosen optional piece. All exercised through Telegram conversations:

- `🔁 Recurrentes` joins the persistent menu. The flow lists the rules, and creates one through
  amount, description, category and day of month, with a confirmation before anything is
  written. A rule can be paused, resumed and deleted; deleting it never touches the expenses it
  already produced.
- `RecurringExpense` is monthly by design: the day of month clamps to the last day of a shorter
  month, an optional end date is inclusive, and `DueDates(today)` returns every missed
  occurrence oldest first without writing anything.
- `RecurringExpenseScheduler` runs every six hours (first pass one minute after startup) and
  applies due occurrences through `IRecurringExpenseService.ApplyDueAsync`. The pass computes
  each user's calendar date with `IUserLocalDate`, inserts an ordinary `Expense` dated that day,
  and moves `LastGeneratedDate` in the same transaction: a second run cannot duplicate a month.
  Only registered when Telegram is configured, because recording an expense nobody is told
  about is worse than waiting.
- `RecurringExpenseNotifier` sends one message per user listing what was registered, and a
  delivery failure is logged, never propagated: the expenses are already committed.

Deferred deliberately: end dates are supported by the domain but not offered by the flow, and
weekly/yearly frequencies would each need their own anchor rule. The other optional pieces
(charts, CSV, scheduled summaries) still start only with a real need.

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

### Phase 5 delivered — expenses core

All exercised through Telegram conversations:

- Guided entry: amount, description, category, date, then a confirmation. Compact entry
  (`35.000 verduras`) joins the same confirmation with the amount and description already
  known; a bare amount asks for the description and an ambiguous one asks which amount.
- The confirmation stores the draft in `pending_actions` and the button carries only its
  identifier. Consumption is a single conditional `UPDATE`, so a double tap registers one
  expense. `↩️ Deshacer` deletes it and works as a global callback, after the flow has ended.
- `📋 Gastos` lists the current month, opens a detail screen, and offers edit (amount,
  description, category, date) and delete behind a confirmation.

Application service: `IExpenseService`. Conversations: `ExpenseConversation` (entry),
`ExpensesConversation` (list, detail, edit, delete) and `ExpenseUndoHandler` (global action).
Storage: `IPendingActionStore` with the `pending_actions` table.

Deferred: date-range history with keyset pagination (design §11.5). The list shows the current
month only; ranges and paging are a follow-up, and the statistics work in Phase 7 will need
period queries anyway.

### Phase 6 delivered — category matching and keyword learning

The deterministic matcher of `docs/TECHNICAL-DESIGN.md` §9, wired into the expense confirmation:

- `ICategoryMatcher` lives in `MyBudget.Application/Matching` and is pure: the caller passes
  category names and aliases, and the matcher returns `Matched`, `Ambiguous` or `None`. No EF,
  no Telegram, no clock.
- Normalisation is NFKC, accents stripped, lowercase, punctuation to spaces, whitespace collapsed
  and leading articles and prepositions dropped. Singular/plural matches for tokens of four or
  more characters are handled at token-comparison time, never by mutating display text.
- Signals and thresholds are those of §9: exact 1.00, phrase 0.85 + 0.05 × coverage (cap 0.95),
  full coverage ≥ 2 tokens 0.80, partial overlap 0.50 × ratio + 0.20, category-name bonus +0.05,
  and a fuzzy token 0.45. Selection: top below `MinimumScore` (0.65) is `None`, a top two closer
  than `AmbiguityMargin` (0.15) is `Ambiguous`, otherwise `Matched`.
- Fuzzy ships **off** by default (`CategoryMatching:EnableFuzzy=false`), is restricted to a
  single-token query with a distance-1 candidate of at least five characters, and ties fall back
  to `Ambiguous`. Options follow the `LocalizationOptions` pattern with a validator and
  `ValidateOnStart`.
- The entry confirmation uses the suggestion: `Matched` pre-selects the category and says so,
  `Ambiguous` asks which category, and `None` asks and then offers to save the typed description
  as a keyword for the chosen category — showing exactly what will be stored, and raising the
  alias conflict prompt when the term already belongs elsewhere. `CategorizationSource` records
  `Matched`, `Ambiguous` or `Manual` accordingly.

Exit criteria met: the matcher corpus including ambiguity, fuzzy off by default, and the
learn-keyword flow with its conflict prompt, plus conversation tests for each path.

### Phase 7 delivered — summary and statistics

All exercised through Telegram conversations:

- `📊 Resumen`: the month's total, the historical allocation of that month, and one line per
  category with a ten-cell text bar and its usage. Month navigation `[← agosto] [septiembre
  2026] [octubre →]`, bounded at the calendar limits the domain allows. A category with
  spending but no allocation shows `—`, overspending shows above 100 %, and a month with no
  budget never divides by zero.
- `📋 Gastos`: the date-range history. Presets for this month, last month, the last three
  months and this year; day grouping with a per-day total; and keyset pagination on
  `(expense_date DESC, id DESC)` with `[⬅️ Anterior] [➡️ Ver más]` over a cursor stack carried
  in the conversation payload. The detail, edit and delete screens hang off each listed row.
- `📈 Estadísticas`: total, expense count and average daily (over the days elapsed, so a month
  in progress is not understated), share per category, daily spending, the five biggest
  expenses, and a comparison with the previous month that states when the period is still in
  progress.

Application: `IReportService` composes the read model; `IExpenseReadRepository` holds the SQL
aggregations (`SUM`/`COUNT` grouped by category and day) and the keyset page. The page query is
raw SQL because the tiebreaker is a `uuid`, which C# cannot compare with an operator, and
PostgreSQL's byte ordering is what the cursor relies on. `DateRange` is an inclusive calendar
range resolved in the user's local zone.

Exit criteria met: snapshot tests assert the rendered summary, statistics and history messages
verbatim, and the period queries are covered against real PostgreSQL in
`ExpenseReportQueryTests` (grouped sums, user isolation, the range boundary, and a full keyset
walk without gaps or duplicates).

### Prompt for the next session — remaining optional pieces

Recurring expenses are done. Charts, CSV and scheduled summaries start only if there is a
real need; nothing in them is a prerequisite for a working bot. Paste this if you decide to
build one:

> Trabajamos en `/home/santidev21/Dev/MyBudget-bot`, un bot de presupuesto personal para
> Telegram (.NET 8 + PostgreSQL, Clean Architecture, modular monolith).
>
> Antes de tocar nada:
> 1. Lee `AGENTS.md` completo y `docs/TECHNICAL-DESIGN.md` (§12 Phase 9, §11 UX flows y
>    §16 testing). Revisa también `docs/BACKUPS.md` y `docs/DEPLOYMENT.md`.
> 2. Mira `git log --oneline`.
> 3. Resúmeme en 5 líneas dónde estamos, qué sigue y las reglas que no se pueden romper.
>
> Contexto: fases 0–9 completas, 750 tests verdes, build sin warnings, `dotnet format` limpio.
> El bot registra gastos, aprende keywords, tiene resumen/estadísticas/historial, gestiona
> reglas recurrentes que el scheduler aplica en su fecha, backups nocturnos verificados con
> restore real y deploy reproducible. `.env` local con bot de DEV en polling, base en
> `127.0.0.1:5435`; el VPS con webhook está desplegado.
>
> Haz **una** de las piezas opcionales que quedan: gráficos, exportación/importación CSV o
> resúmenes programados. Elige la que tenga una necesidad real, no todas. Alcance de cada
> opción en `docs/TECHNICAL-DESIGN.md` §12.
>
> Reglas que no se rompen: dinero `long` exacto; `ExpenseDate` es `DateOnly` en hora local;
> ownership con `userId` primero y FKs compuestas; la historia no se borra (desactivar); nada de
> IA para categorizar; texto de usuario sólo en `Messages.resx` + `MessageKeys`; los casos de
> uso en Application; las conversaciones de Telegram sólo recogen input y renderizan.
>
> Trabaja en pasos cortos: implementa una cosa, corre
> `dotnet build MyBudget.sln && dotnet test MyBudget.sln && dotnet format MyBudget.sln --verify-no-changes`,
> muéstrame el resultado y sigue. No acumules turnos larguísimos.

### Earlier handoff (Phase 7, kept for reference)

Paste this to start the next session:

> Trabajamos en `/home/santidev21/Dev/MyBudget-bot`, un bot de presupuesto personal para
> Telegram (.NET 8 + PostgreSQL, Clean Architecture, modular monolith).
>
> Antes de tocar nada:
> 1. Lee `AGENTS.md` completo y `docs/TECHNICAL-DESIGN.md` (§11.4 Resumen, §11.5 Historial,
>    §11.8 Estadísticas, §12 Phase 7, §16 testing).
> 2. Mira `git log --oneline`.
> 3. Resúmeme en 5 líneas dónde estamos, qué sigue y las reglas que no se pueden romper.
>
> Contexto: fases 0–6 completas, 671 tests verdes, build sin warnings, `dotnet format` limpio.
> El bot ya registra gastos (guiado y compacto), sugiere categoría con el matcher determinista
> y aprende keywords. `.env` local con bot de DEV en polling, base en `127.0.0.1:5435`; el VPS
> con webhook está desplegado.
>
> Haz la **Phase 7: resumen y estadísticas**. Alcance:
> - `📊 Resumen` mensual: total, uso por categoría con barras de texto, y navegación de mes
>   `[← Agosto] [Septiembre 2026] [Octubre →]` leyendo la asignación histórica de cada mes.
> - Historial por rangos con paginación keyset en `(expense_date DESC, id DESC)`, agrupado por
>   día (diferido de Phase 5, §11.5).
> - Estadísticas: por categoría con participación, gasto diario, gastos más grandes, promedio
>   diario, y comparación de periodos que advierte cuando un periodo está incompleto.
>
> Reglas que no se rompen (resumen): dinero `long` exacto; `ExpenseDate` es `DateOnly` en hora
> local; ownership con `userId` primero y FKs compuestas; la historia no se borra (desactivar);
> nada de IA para categorizar; texto de usuario sólo en `Messages.resx` + `MessageKeys`; los
> casos de uso en Application; las conversaciones de Telegram sólo recogen input y renderizan.
>
> Foco:
> - Las consultas de periodo y las sumas pertenecen a Application (o a un read model en SQL),
>   nunca a la conversación. Al personal scale el total se deriva con `SUM`, jamás se almacena.
> - Un mes sin presupuesto muestra uso `—`, nunca una división por cero; sobregirar se muestra,
>   no se bloquea.
> - Un periodo incompleto (mes en curso) debe decirlo en la comparación.
> - Barras de texto primero; los gráficos son Phase 9.
>
> Trabaja en pasos cortos: implementa una cosa, corre
> `dotnet build MyBudget.sln && dotnet test MyBudget.sln && dotnet format MyBudget.sln --verify-no-changes`,
> muéstrame el resultado y sigue. No acumules turnos larguísimos.
>
> Criterio de salida: snapshot tests de los mensajes renderizados y consultas de periodo
> cubiertas contra PostgreSQL real.

### Commands to verify any change

```bash
dotnet build MyBudget.sln                 # zero warnings
dotnet test MyBudget.sln                  # everything
dotnet format MyBudget.sln --verify-no-changes
```

Running it locally: fill `.env` (see `scripts/init-telegram-env.sh`), start the database with
the local overlay, and `dotnet run --project src/MyBudget.Api`. The app reads `.env` in
development, so no variable is repeated on the command line.
