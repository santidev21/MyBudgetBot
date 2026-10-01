# Implementation gotchas

Hard-won details that are easy to regress. [`AGENTS.md`](../../AGENTS.md) keeps a one-line-per-
group summary; this file holds the full list with the reasoning behind each entry. Update it
whenever a gotcha is learned or disproven.

## EF Core and keys

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

## Configuration and hosting

- Configuration binding does not turn a single value into an array: `AllowedUserIds` is a
  `string` with explicit parsing. A blank value counts as unset, so `.env` can fill it.
- Hosted services are singletons; resolve scoped services from a scope per iteration. Only a
  test that builds the real host catches a mistake (`ApiTelegramWiringTests`).
- `Options.Create` is ambiguous inside files importing `MyBudget.Telegram.Options`; qualify it.
- The runtime image is Debian for ICU and tzdata; `external: false` in the local compose overlay
  is required; both `app` and `migrator` keep `image: mybudget-app`.
- Docker cannot publish a host port for a container whose only network is internal.

## Text, resources and payloads

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

## Dates, money and reporting

- `IUserLocalDate` is the only UTC-to-local conversion; an unusable stored zone falls back to
  UTC. `ExpenseDate` is a `DateOnly`; report sums are derived in SQL, never stored.
- The matcher's 0.20 partial-overlap floor and epsilon comparison are load-bearing; signals are
  additive per query/keyword and the category takes its best term.
- A budget-alert marker is recorded whether or not the notification is delivered: the marker
  stops the bot repeating itself.

## Operations

- The EF migration history table is mixed case: quote `"__EFMigrationsHistory"` in raw SQL.
- `pg_restore` exits 0 unless `--exit-on-error`; the drill must actually restore, not just list.
- Pre-deploy and nightly dumps share the backup volume.

## Recurring and charts

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
  dependencies; the font folds accents (`á` renders as `a`) and leaves what it does not know
  blank, so the icon and the exact name stay in the caption, where the phone's font draws them.
  A horizontal bar is a share of the `total` it is given, never a fraction of the longest bar:
  the percentage in the row is what the length shows, and two rows are comparable. The text
  screens always carry the exact numbers.

## Settings, budgets and routing

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
