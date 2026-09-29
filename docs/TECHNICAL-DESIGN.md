# MyBudget-bot — Technical Design

Design record for the project. Written in English per the repository language rule.
It states the decisions that matter, including the places where the original brief was
changed and why.

---

## 1. Recommended architecture

A **modular monolith**: one deployable, clean layering, no microservices, no message
broker, no cache, no Kubernetes. It runs on one personal VPS and stores one person's
financial ledger; anything more is unjustified complexity.

```
Telegram (HTTPS webhook)
        |
        v
MyBudget.Api            host: webhook endpoint, health, Serilog, DI, --migrate
MyBudget.Telegram       presentation: dispatch, conversations, rendering
MyBudget.Application    use cases and policies: parsing, matching, summaries
MyBudget.Domain         entities, invariants, pure budget arithmetic
MyBudget.Infrastructure EF Core 8 + Npgsql, configurations, migrations, repositories
        |
        v
PostgreSQL 16
```

Dependency rule, enforced by `tests/MyBudget.Architecture.Tests`:

| Layer | May depend on |
|---|---|
| Domain | nothing |
| Application | Domain |
| Telegram | Application, Domain (never EF Core, never Infrastructure) |
| Infrastructure | Application, Domain (never Telegram) |
| Api | everything |

Why it is shaped this way: the only thing that must stay out of Telegram code is business
rules. Parsing, matching, budget arithmetic and summaries therefore live in
Application/Domain and are testable with no Telegram reference and no database.

No MediatR, no CQRS framework: with roughly twenty use cases, a handler bus adds
indirection without buying anything. Read models get their own interfaces where
aggregation belongs in SQL.

## 2. Solution and project structure

```
MyBudget.sln
Directory.Build.props        nullable, warnings as errors, deterministic builds
Directory.Packages.props     central package versions
src/MyBudget.Domain
src/MyBudget.Application
src/MyBudget.Infrastructure
src/MyBudget.Telegram
src/MyBudget.Api
tests/MyBudget.Domain.Tests            pure unit tests
tests/MyBudget.Application.Tests       contract tests + future parser corpus
tests/MyBudget.Infrastructure.Tests    Testcontainers PostgreSQL integration
tests/MyBudget.Telegram.Tests          conversation and rendering tests
tests/MyBudget.Architecture.Tests      layering enforcement
docker/app/Dockerfile
docker/postgres/init/01-create-users.sh
scripts/deploy.sh
docs/
```

No frontend. The HTTP surface is the webhook and health endpoints only; Swagger is never
published.

## 3. Domain model

```
User                  Id, TelegramUserId (unique), Username?, DisplayName?,
                      Currency, TimeZone, Language, CreatedAt, UpdatedAt
BudgetCategory        Id, UserId, Name, Icon, IsActive, Aliases[], CreatedAt, UpdatedAt
CategoryAlias         Id, UserId, CategoryId, Alias, NormalizedAlias, CreatedAt
MonthlyBudget         Id, UserId, Year, Month, Allocations[], CreatedAt, UpdatedAt
MonthlyBudgetCategory Id, UserId, MonthlyBudgetId, CategoryId, Amount, CreatedAt, UpdatedAt
Expense               Id, UserId, CategoryId, Amount, Description?, ExpenseDate,
                      CategorizationSource, CreatedAt, UpdatedAt
MonthPeriod           value object (Year, Month) with Previous/Next/Contains/FirstDay/LastDay
BudgetLine / BudgetMath   pure derived values: spent, remaining, usage, overage
CategorizationSource  Manual | Matched | Ambiguous | Learned
```

Invariants live in the entities: amounts are positive and bounded, names are non-empty and
bounded, descriptions are trimmed, future expense dates are rejected, allocations cannot be
negative. `today` is passed in rather than read from a static clock, so the rules are
testable and time-zone correct.

**Money.** Entities persist `long Amount` in the currency's minor units: whole pesos for COP,
so 35.000 COP is `35000`. No wrapper type was introduced — amounts are `long` and money
handling lives in dedicated parser and formatter services, which is where the real risk is.
A `Money` value object would have added a type without adding a guarantee, since the
invariant that matters (positive and bounded) is already enforced by the entity and the
database. Multi-currency later means one additive `currency char(3)` column on `expenses`
plus a backfill; `CurrencyDefinition` already carries decimal places and separators, so the
parser and formatter need no change.

**`CategorizationSource`** records how a category was chosen. It costs one small column and
makes suggestion quality measurable later without logging any user text.

Deliberately not modelled: a stored total, a stored remaining, income, accounts, payments.

## 4. Database schema

`snake_case` tables and columns, `uuid` keys, `timestamptz` instants, `date` calendar dates,
`bigint` money. Constraint names are stable and asserted by tests.

Structural decisions:

1. **Composite foreign keys make cross-user references impossible.**
   `(category_id, user_id) REFERENCES categories (id, user_id)`. EF Core cannot express an
   FK to a non-primary unique key, so these are added with `migrationBuilder.Sql(...)` and
   verified by integration tests. A unique constraint on `(id, user_id)` exists purely to
   be their target.
2. **`ON DELETE NO ACTION` on category references** so a category with history cannot be
   deleted. Consequence: PostgreSQL will not reliably cascade a one-statement user delete
   across the graph, so user erasure is explicit, ordered and transactional
   (`IUserDataEraser`).
3. **Functional unique index** `uq_categories_user_name ON categories (user_id, lower(btrim(name)))`
   so a user cannot own two categories differing only by case or padding.
4. **Duplicate aliases across categories are allowed on purpose.** Ambiguity ("comida" =
   restaurants or groceries) is modelled in data and resolved by asking the user, not by
   silently picking one. Uniqueness is per `(category_id, normalized_alias)`.

Tables: `users`, `categories`, `category_aliases`, `monthly_budgets`,
`monthly_budget_categories`, `expenses`. Operational tables arrive with later phases:
`telegram_updates` (idempotency inbox), `conversation_states`, `pending_actions`.

Key constraints:

| Table | Constraint |
|---|---|
| `users` | `telegram_user_id` unique; currency `^[A-Z]{3}$`; language format |
| `categories` | name non-blank and ≤ 60; icon ≤ 16; name unique per user, case-insensitive |
| `category_aliases` | alias non-blank and ≤ 60; unique per category and normalized alias |
| `monthly_budgets` | unique `(user_id, year, month)`; month 1–12; year 2000–2100 |
| `monthly_budget_categories` | amount ≥ 0; unique `(monthly_budget_id, category_id)` |
| `expenses` | `0 < amount ≤ 999999999999`; description ≤ 500; source in a fixed set |

## 5. Historical monthly budget model

Alternatives considered:

| Model | Verdict |
|---|---|
| Limit stored on `categories` | Rejected: editing October would rewrite September's report. |
| **Per-month allocation table** | **Chosen.** Each month holds its own snapshot. |
| Effective-dated allocations | Rejected: supports date ranges the product does not have, with much harder overlap semantics. |
| Event sourcing | Rejected: massive over-engineering for one user. |

```
monthly_budgets(user, 2026-09) ──< monthly_budget_categories(Market, 1_000_000)
monthly_budgets(user, 2026-10) ──< monthly_budget_categories(Market, 1_500_000)
```

- A September report reads September's allocation row. October is unreachable from it.
  Covered by `HistoricalBudgetTests`.
- **The monthly total is derived** (`SUM`), never stored, never cached. At personal scale
  (tens of rows per month) this is a single index scan. A denormalised total would be a
  liability with no benefit.
- **Rollover is explicit.** `GetOrCreate` creates an empty month. The bot offers "copy last
  month's budget" as a deliberate action; nothing is copied silently.
- **Past months are immutable in the product**: budgets can be edited for the current and
  future months only. The database does not enforce that (it would block data repair), so
  the application layer does.
- Deactivating a category hides it from pickers and from new allocations while every
  historical allocation and expense keeps working.
- An expense may be recorded in a month with no budget: budget `0`, spent `> 0`, usage
  shown as `—`.

## 6. Telegram conversation and state architecture

Update pipeline (Phase 3):

1. Verify the `X-Telegram-Bot-Api-Secret-Token` header.
2. Claim the update in `telegram_updates` with `INSERT ... ON CONFLICT DO NOTHING`.
   Zero rows inserted means it is a duplicate, and the response is 200.
3. Reject non-private chats, users outside `ALLOWED_TELEGRAM_USER_IDS`, and updates older
   than a stale window (Telegram can replay up to 24h of queued updates after downtime).
4. Resolve or create the user by `telegram_user_id`.
5. Take a per-user PostgreSQL advisory lock so one user's updates are processed serially.
6. Route: callback query → conversation callback or global action; command → command
   handler; text → active conversation, then compact-expense parsing, then menu intent.
7. Send replies after the transaction commits, then mark the update processed.

Return 200 for handled, duplicate and ignored updates. Return 500 only for transient
infrastructure failures where a Telegram retry genuinely helps; the inbox makes the retry
safe.

**Conversations** are explicit state machines:

```csharp
public interface IConversation
{
    string Name { get; }
    Task<ConversationTurn> StartAsync(ConversationContext ctx, CancellationToken ct);
    Task<ConversationTurn> HandleTextAsync(ConversationContext ctx, UserText text, CancellationToken ct);
    Task<ConversationTurn> HandleCallbackAsync(ConversationContext ctx, UserCallback cb, CancellationToken ct);
}
```

State, payload and expiry are persisted per user in `conversation_states`, so a conversation
survives a restart or a deploy. `ConversationTurn` returns a list of Telegram-agnostic
`BotResponse` values; conversations never touch `ITelegramBotClient`, which makes them unit
and snapshot testable.

Telegram's `Conversation`/wizard helpers were rejected: in-memory state is not restart-safe
and is awkward to test. Explicit state machines are more code and far easier to reason
about.

**Callback data** is capped by Telegram at 64 bytes, so it carries only
`v1|action|<pendingActionId>`. The draft lives in `pending_actions` and is re-loaded,
ownership-checked and consumed exactly once
(`UPDATE ... WHERE consumed_at IS NULL ... RETURNING *`). No amount, category or date is
ever trusted from a callback. `pending_actions` arrives with the confirmation flow in Phase 5;
Phase 3 persists only `conversation_states`.

Two behaviours settled during implementation:

- **`processed` and `ignored` are both terminal inbox states.** Only `failed` reopens a
  claim. Treating `ignored` as claimable would act on an update that was skipped on purpose.
- **A callback is acknowledged immediately, without a toast.** Telegram accepts exactly one
  answer per query, and stopping the spinner promptly matters more than a decorative message;
  the substantive reply is the message that follows. A `CallbackAcknowledgement` field was
  designed, implemented, and then removed as dead configuration.

Behaviours settled in Phase 4:

- **A tap on the persistent menu aborts the active flow.** The router matches the menu before
  delegating text to a conversation. Delegating first would swallow a label such as
  `📊 Resumen` as flow input and, in the category flow, store it as a category name.
- **A draft that identifies a large value carries the position, not the value.** Telegram caps
  callback data at 64 bytes, so the alias screen lists remove buttons by index into a list held
  in the conversation payload and removes the keyword by term afterwards. No user text is
  trusted from a callback: the category is always re-read and ownership-checked.
- **A conversation payload that cannot be read is treated as empty.** `jsonb` is canonicalised
  by PostgreSQL and may have been written by an older version of a flow.

Behaviours settled in Phase 5:

- **A confirmation is claimed, not re-read.** The draft lives in `pending_actions`, and the
  callback carries only its identifier (`v1|expense|<id>`). Consumption is one conditional
  `UPDATE` over ownership, expiry and `consumed_at IS NULL`, so a double tap or a replayed
  callback registers one expense. `ExecuteUpdateAsync` bypasses the change tracker, so the draft
  is read back untracked.
- **Global callbacks run when no conversation is active.** `↩️ Deshacer` sits on the message
  that follows a completed registration, so the router cannot require a conversation for it.
  `IGlobalCallback` handlers are tried before a callback is declared expired; an unrecognised
  callback still expires.
- **Compact and guided entry share one confirmation.** `ICompactExpenseParser` decides amount and
  description; the conversation decides the rest. Two plausible amounts ask instead of guessing,
  and a bare number answers "how much?" and asks for the description.
- **A listing that short-circuits keeps its notices.** The month list reported "nothing to show"
  and dropped the "deleted" notice when a delete emptied the month.

## 7. Monetary parsing architecture

`IMoneyParser` is a pipeline of independently testable stages, and it never throws:

```csharp
MoneyParseResult Parse(string? input, CurrencyDefinition currency)

Success(Money value, string normalizedDisplay, MoneyInputForm form)
Ambiguous(raw, candidates, reason)
Invalid(raw, reason)
```

1. `MoneyInputPreprocessor` — trim, NFKC, normalise NBSP/thin spaces, lowercase, strip
   currency tokens (`$`, `cop`, `peso(s)`), strip trailing punctuation, reject a leading
   minus.
2. `MagnitudeSuffixResolver` — `k`/`mil` ×1.000, `m`/`mm`/`millon(es)` ×1.000.000.
   Resolved before separators, so `1.5 millones` works.
3. `SeparatorAnalyzer` — the only place `.` and `,` are interpreted. Uses `BigInteger`/
   `decimal` internally, never `double`.
4. `MoneyValueValidator` — must be a whole number of pesos (no silent rounding), `> 0`,
   within the same bound as the database.
5. `MoneyParseResultFactory` — builds the result and the display form through
   `IMoneyFormatter`.

`CurrencyDefinition` is data (`Code`, `Symbol`, `DecimalPlaces`, `GroupSeparator`,
`DecimalSeparator`, `Suffixes`), so adding a currency is an entry, not a new parser.

Compact entry extraction (§ "35000 verduras") is a separate `ICompactExpenseParser`:
tokenise, find numeric spans (including two-token `35 mil`), take the most money-like span as
the amount, strip leading prepositions and articles, treat the rest as the description. Two
equally plausible amounts → `Ambiguous`, fall back to the guided flow.

## 8. Colombian number-format parsing rules

COP has zero decimal places; Colombian convention is `.` grouping and `,` decimal. The
table below is the implemented behaviour, and every row is a test.

| Input | Result | Value |
|---|---|---|
| `35000` | Success (Plain) | 35000 |
| `35.000` | Success (StandardGrouping) | 35000 |
| `35,000` | Success (ToleratedGrouping) | 35000 |
| `35 000`, `35<nbsp>000`, `35<thin space>000` | Success (SpaceGrouped) | 35000 |
| `$35.000`, `$ 35.000`, `COP 35.000`, `35000 pesos`, `35000pesos` | Success | 35000 |
| `+35.000`, `35.000.` | Success | 35000 |
| `35k`, `35 k`, `35K`, `35 mil`, `35mil`, `35 mm` | Success (SuffixScaled) | 35000 / 35 000 000 |
| `1,5k`, `1.5k` | Success (SuffixScaled) | 1500 |
| `2 millones`, `1,5 millones de pesos` | Success (SuffixScaled) | 2 000 000 / 1 500 000 |
| `1.500.000` | Success (StandardGrouping) | 1 500 000 |
| `1,234,567` | Success (ToleratedGrouping) | 1 234 567 |
| `0.500` | Success (StandardGrouping) | 500 |
| `3,5`, `3,50`, `1.50`, `1.234,56`, `1,234.56` | Invalid (FractionNotAllowed) | — |
| `1.234.56`, `35.00.00.0`, `1000.000`, `1.1234`, `1.234,56,78` | Invalid (MalformedGrouping) | — |
| `0`, `0.000` | Invalid (NonPositive) | — |
| `-35.000` | Invalid (Negative) | — |
| `1000000000000`, `1.234.567.890.123`, a 25-digit number | Invalid (TooLarge) | — |
| ``, `   `, `$` | Invalid (Empty) | — |
| `abc`, `verduras`, `mil`, `k`, `35 mil mil`, `1.5m`, `35,000 kg` | Invalid (NotANumber) | — |
| `1.500` in a currency with 2 decimal places | Ambiguous | 150 000 or 150 minor units |

The rules, in the order they are applied:

1. **Normalise.** Unicode compatibility form, lowercase, every space-like character
   (including non-breaking and thin spaces) to ASCII space, strip `$` and the currency
   symbol, strip spoken currency words (`pesos`, `colombianos`, `COP`) and the connectors
   `de`/`del`, then trim trailing punctuation.
2. **Strip the magnitude suffix**, longest match first: `k`/`mil` ×1.000,
   `mm`/`millon`/`millón`/`millones` ×1.000.000. A bare `m` is deliberately **not** a
   suffix, so `35 m` can never silently become 35 million.
3. **Resolve separators.** Both kinds present means the last one is the decimal separator
   and the other is grouping, and the decimal separator may appear only once. With exactly
   one occurrence of one kind: three digits after it is grouping, one or two is decimal,
   anything else is malformed. Two or more occurrences of one kind are all grouping. Spaces
   are always grouping. Grouping is valid when the leading group has one to three digits and
   every following group has exactly three; a single unseparated group is unconstrained.
4. **Scale exactly.** `value × multiplier × 10^DecimalPlaces` must be a whole number, greater
   than zero, and at most 999 999 999 999 — the same bound as the `ck_expenses_amount`
   constraint. There is no rounding: an amount the currency cannot express is rejected.
5. **Ambiguity** is reported only when the currency has decimal places, a lone separator is
   followed by exactly three digits, and both readings are legal amounts. A currency with no
   decimal places can never satisfy that, which is why COP users are never interrupted.

Two deliberate deviations from the brief:

- `3.500` and `3,500` are read as `3500` without asking. In a COP-only Colombian context
  `3,5` pesos is not a plausible intent, and the confirmation screen already echoes the
  normalised value. Prompting here would add friction to the single most common input.
- There is no "confirm the magnitude" state. An amount above the accepted maximum is
  `TooLarge`; everything else is already shown back to the user before it is saved.

Formatting is done with explicit separator characters rather than ICU output, so it cannot
drift with the host: `3500 → $3.500`, `35000 → $35.000`, `1500000 → $1.500.000`.
Percentages use one decimal and a comma (`60,6 %`), and whole percentages have no decimal
part (`72 %`).

## 9. Category matching algorithm

Deterministic and pure: the caller passes the user's aliases and category names.

**Normalisation**: NFKC → strip accents → lowercase (invariant) → punctuation to spaces →
collapse whitespace → strip leading articles and prepositions → optional plural folding for
tokens of four or more characters (matching only, never display).

**Signals** (additive, capped at 1.0)

| Signal | Score |
|---|---|
| Exact full match | 1.00 |
| Category name match | +0.05 over the equivalent alias signal |
| Alias appears as a phrase | 0.85 + 0.05 × coverage, cap 0.95 |
| Full token coverage, ≥ 2 tokens | 0.80 |
| Partial token overlap | 0.50 × ratio + 0.20 |
| Fuzzy token (≥ 5 chars, edit distance exactly 1, at most one) | 0.45 |

**Selection**

```
top score < 0.65                       -> None       (ask the user)
top score - second score < 0.15        -> Ambiguous  (ask which category)
otherwise                              -> Matched    (confirm the suggestion)
```

`"comida"` aliased to two categories is therefore always `Ambiguous`, never a silent guess.
When nothing matches, the user is asked and offered the chance to save the term as a keyword
for the chosen category — after being shown exactly what will be stored, and with a
conflict prompt if the term already belongs elsewhere.

**Deliberate deviation from the brief:** fuzzy matching ships **off by default**
(`CategoryMatching:EnableFuzzy=false`). A wrong category silently corrupts reports; asking
costs two seconds. When enabled it is restricted to a single-token query with a unique
distance-1 candidate, and ties fall back to `Ambiguous`.

Implementation notes. Signals are additive per query/keyword pair and the category takes its best
term; the name bonus is applied to the name term before that maximum, and summing across keywords
would let a category with many aliases buy a score. The partial-overlap term keeps its 0.20 floor
even at zero overlap, which is what lets a lone fuzzy match (0.20 + 0.45) reach the 0.65
threshold; scores are compared with a small epsilon for that reason. The unrecognized description
that `None` offers to learn travels in the conversation payload and is only stored after the user
picks a category, and the conflict prompt reuses the alias screen's wording.

## 10. Localization architecture

`IUserMessages.Get(language, key, args)` over `.resx` resources, with the language passed
**explicitly** rather than read from `CultureInfo.CurrentUICulture`.

Why not `IStringLocalizer`: it resolves culture from ambient state. A Telegram bot has no
HTTP request culture, so every entry point would have to mutate async-local state correctly,
and any miss silently returns the wrong language. Explicit is smaller and testable, and a
test proves the ambient culture is ignored.

- `MessageKeys` holds `const string` identifiers, all English. Resource *values* are Spanish.
- **Spanish is the neutral resource set** (`Resources/Messages.resx` plus
  `<NeutralResourcesLanguage>es</NeutralResourcesLanguage>`). That means the default language
  needs no satellite assembly, and a language without resources falls back to Spanish rather
  than to nothing. Adding a language means adding `Messages.<culture>.resx` and listing the
  culture in `Localization:SupportedLanguages`.
- Resolution is `language → default language → the key itself`. A user never sees a blank
  message, and an unusable language tag (which comes from the database and is therefore not
  trusted) falls back instead of raising.
- A missing translation fails the build: tests assert that the declared constants, the
  `MessageKeys.All` list and the resources on disk agree exactly, in every direction, and that
  no value is blank.
- Resources hold plain text with placeholders; the presenter HTML-escapes each interpolated
  value before substitution and applies markup itself. A description containing `<b>` or
  `&` can therefore never alter the message structure.
- **Default category names are user data**: seeded from the catalog at onboarding and frozen
  at creation. Changing the user's language later does not rename existing categories.
- Month and weekday names come from the catalog, not from ICU. `MessageKeys.Months` is the
  calendar order, so a period renders as `septiembre 2026` with no culture dependency.

## 11. Main Telegram UX flows

- **HTML parse mode** throughout (MarkdownV2 escaping is a trap for user text).
- **Persistent reply keyboard** for the six sections, so the text field stays free for
  compact entry such as `35000 verduras`.
- `/start /help /resumen /gasto /gastos /categorias /estadisticas /config /cancel`.
  Identifiers English, descriptions Spanish.
- Every confirmation offers `[✅ Registrar] [🏷️ Cambiar categoría] [✏️ Editar] [📅 Fecha]`,
  and the registration reply offers `[↩️ Deshacer]`.
- `❌ Cancelar` is available in every multi-step flow.

1. **Onboarding** — welcome, timezone confirm (short list, default Bogotá), optional seed of
   default categories, optional budgets. Fully skippable. No currency step: with COP-only
   it would be a control that changes nothing.
2. **Add expense, guided** — amount → description → suggestion → confirmation. Date defaults
   to today in the user's time zone; `[📅 Fecha]` offers today, yesterday, another date.
   Future dates are rejected.
3. **Add expense, compact** — free text parsed into amount and description, then the same
   confirmation. Low confidence falls back to the guided flow with what was parsed kept.
4. **Summary** — totals, usage, per-category text bars, month navigation
   `[← Agosto] [Septiembre 2026] [Octubre →]` reading each month's historical allocation.
5. **History** — date-range presets, keyset pagination on `(expense_date DESC, id DESC)`,
   day grouping.
6. **Expense detail** — amount, description, category, date; edit, delete (with
   confirmation), back.
7. **Categories** — create, rename, icon, aliases with conflict handling, activate/
   deactivate, set this month's allocation, copy last month. Deletion is not offered;
   deactivation is, with an explanation. Only the current month is editable, and the bot says
   so rather than silently refusing; copying is always an explicit action.
8. **Statistics** — by category with share, daily spending, largest expenses, average daily,
   period comparison that states when a period is incomplete.
9. **Configuration** — language, time zone, currency (read-only until multi-currency),
   delete my data.
10. **Operational alerts** — backup verification failures are sent to the admin user.

## 12. MVP implementation phases

| Phase | Scope | Exit criteria |
|---|---|---|
| **0 Foundation** | Solution, build settings, central packages, Serilog, options validation, health endpoints, Docker, least-privilege roles, `--migrate`, Testcontainers fixture | Stack runs; healthchecks green; CI green — **done** |
| **1 Domain + persistence** | Entities, EF configurations, raw-SQL composite FKs, repositories, interceptor | Integration tests for constraints, user isolation, historical budgets — **done** |
| **2 Money, dates, i18n** | Parser, formatter, compact parser, date parser, currency registry, message catalog | Parser corpus + property tests; ≥ 95 % coverage on money code — **done** (97,5 % money, 100 % dates) |
| **3 Telegram plumbing** | Webhook, inbox idempotency, allowlist, advisory lock, conversation store and router, menu, onboarding | Local polling answers `/start`; duplicate update creates one row — **done** |
| **4 Categories & budgets** | Category CRUD, aliases, allocations, copy previous month | History tests; flow tests — **done** |
| **5 Expenses core** | Guided and compact entry, pending actions, confirmation, list, detail, edit, delete, undo | End-to-end flow tests — **done** |
| **6 Matching** | Matcher, ambiguity, keyword learning, conflicts | Corpus including ambiguity; fuzzy off by default — **done** |
| **7 Summary & statistics** | Dashboard, ranges, statistics, comparison | Snapshot tests of rendered messages — **done** |
| **8 Hardening** | Backups with verification, runbook, rate limits, deploy automation | Restore drill performed; deploy from clean checkout — **done** |
| **9 Optional** | Charts, recurring expenses, CSV, scheduled summaries | Not started without a real need |

## 13. Important edge cases

- An expense at 20:00 Bogotá must land on the local calendar day, including month ends.
- Colombia has no DST, but nothing may hardcode UTC−5.
- Changing the user's time zone must not move historical `expense_date` values.
- Leap years, month lengths, `25/09`, `25-09-2026`, `25 de septiembre`, `hoy`, `ayer`.
- Budget `0` with expenses → usage `—`, never a division error.
- Overspending is displayed, never blocked. The budget is a tracking tool, not an
  authorisation system.
- A month with no budget may still receive expenses.
- Deactivated categories keep their history and still render in old reports.
- Re-creating a disabled category's name offers reactivation rather than a duplicate.
- Duplicate Telegram updates produce exactly one expense.
- Concurrent updates from one user are serialised by an advisory lock.
- Updates replayed after downtime are ignored with an explanation.
- Message length > 4096, callback data > 64 bytes, and Telegram 429 with `retry_after`.
- The bot being blocked by the user must not crash processing.
- Descriptions containing HTML, control characters or zero-width characters are sanitised
  and escaped.
- A bare number with no active conversation starts the add-expense flow rather than being
  treated as a description.
- Every monetary input in §8's table, including `mil` alone, `,,,`, `$`, and 60-digit input.

## 14. Security considerations

**Identity and isolation**

- Identity is `telegram_user_id` from the authenticated update. Usernames are never identity.
- `ALLOWED_TELEGRAM_USER_IDS` allowlist; private chats only; unknown users are refused in
  Spanish and logged by id only.
- Every scoped repository method takes `userId` first, so forgetting the scope is a compile
  error.
- Composite foreign keys make cross-user references impossible even if application code is
  wrong, and a test suite asserts it.
- Architecture tests forbid Telegram → EF Core and Application → EF Core.

**Ingress**

- Webhook path contains a random segment and requires the secret token header, compared in
  constant time.
- The gateway restricts the webhook to POST, caps the body and does not log request bodies.
- `/health/live` is minimal and public; `/health/ready` and `/health` are restricted to the
  Docker network ranges.
- Webhook registration is an explicit deployment step, never automatic on startup, so
  `drop_pending_updates` can never be triggered by accident.

**Secrets and logging**

- Secrets only through environment variables. `.env` is untracked and dockerignored, and CI
  asserts it.
- Serilog redacts `botToken`, `password`, `connectionString`, `secret` and `Authorization`.
- No financial content at Information level: ids, counts, durations and event names only.
- Log event names are English and stable: `TelegramUpdateReceived`, `TelegramUpdateProcessed`,
  `TelegramUpdateFailed`, `ExpenseCreated`, `ExpenseUpdated`, `ExpenseDeleted`,
  `CategoryCreated`, `CategoryUpdated`, `BudgetChanged`, `MoneyParseAmbiguous`,
  `CategoryMatchAmbiguous`, `BackupVerificationFailed`.

**Abuse and data protection**

- Per-user inbound throttle and 429-aware outbound sending.
- Callback replay prevented by consume-once pending actions.
- Database roles: `mybudget_migrator` owns the schema, `mybudget_app` is DML-only, neither is
  a superuser, and the database is not published to the host. Verified in the running stack.
- Colombian Ley 1581 (habeas data) applies to personal financial data, so erasure is an
  explicit, tested, transactional operation and off-site backups are encrypted.

## 15. Deployment architecture

Aligned with `vps-gateway/docs/STANDARD.md`.

```
Telegram ──HTTPS──▶ vps-gateway (nginx :443)
                        │ proxy_pass http://mybudget:8080
                        ▼
                   mybudget          (mybudget-net + mybudget-internal-net)
                        │
                   mybudget-db       (mybudget-internal-net only)
```

- `mybudget-net`: external, shared with the gateway, carries ingress and the egress needed to
  call `api.telegram.org`.
- `mybudget-internal-net`: internal, database only.
- `mybudget-migrator`: one-shot, runs `--migrate`, then exits. The application never migrates
  on start. Migrations run under a PostgreSQL session advisory lock, so two migrator
  containers started at the same time cannot race on the schema; the second finds it
  already current.
- Both `app` and `migrator` share the `mybudget-app` image, so Compose builds once.
- Options validation runs at startup (`ValidateOnStart`), so a bad `Localization:DefaultTimeZone`
  or `DefaultLanguage` aborts boot with a clear message instead of breaking the first user.
- The database is provisioned with two least-privilege roles by
  `docker/postgres/init/01-create-users.sh`, which runs once on an empty volume. Changing a
  password there later has no effect; rotate with `ALTER ROLE`.
- No host ports in production. Local development publishes `127.0.0.1:5432` and
  `127.0.0.1:8091` only.
- The runtime image is Debian, not Alpine, because `es-CO` formatting needs ICU and
  `TimeZoneInfo` needs tzdata.

Gateway steps for a new deployment: create the DNS record, add `mybudget-net` to the gateway
compose, issue the certificate with certbot, copy
`docs/gateway/mybudget.santidev21.tech.conf` into `sites-enabled/`, then `nginx -t` and
reload. That config is versioned in this repository because it decides this service's
external exposure: only `/health/live` is public, `/health/ready` and `/health` are
restricted to private ranges, and the webhook is POST-only, unlogged and rate limited.

Registering the bot with Telegram is a separate, explicit step:

```bash
dotnet run --project src/MyBudget.Api -- --configure-telegram
```

It sets the Spanish command menu and the webhook. It is never done at startup: calling
`setWebhook` on every boot, with `dropPendingUpdates`, would silently discard updates the
user is waiting on. Request logging is downgraded for the webhook path so the secret URL
segment does not end up in access logs.

Observability is deliberately limited to structured JSON logs plus health endpoints.
OpenTelemetry and Prometheus are out of scope for one VPS and one user.

## 16. Testing strategy

Stack: xUnit, FluentAssertions, NSubstitute, Testcontainers (PostgreSQL), Respawn, FsCheck,
NetArchTest, coverlet.

Rules:

- **Never the EF in-memory provider.** It ignores CHECK, UNIQUE and FOREIGN KEY constraints,
  which are the guarantees under test. Integration tests run against a real PostgreSQL
  container, exactly as CI does.
- One container per test run, migrated once, Respawn reset between tests with
  `__EFMigrationsHistory` excluded.
- Raw SQL is used deliberately to create states the domain forbids, proving the database
  refuses them when application code is bypassed.
- Money parsing will be a data-driven corpus plus FsCheck invariants: `parse` never throws,
  and `parse(format(x)) == x` for random values in range.
- Snapshot tests for rendered messages will run against a fake message catalog, with a small
  number of explicit Spanish assertions.
- Coverage gates: Application and Domain ≥ 90 %, money code ≥ 95 %. Measured: Money 97,5 %,
  Dates 100 %, Text 100 %, Configuration 100 %, Localization 97,3 %. The lines that remain
  uncovered are defensive guards the public pipeline cannot reach, kept for future callers
  and listed in the pull request that added them rather than deleted to make a number look
  better.

Coverage today: money parsing and formatting (corpus plus FsCheck round-trip and
never-throws properties), date parsing, the user's local calendar date, the message catalog, the
Telegram pipeline and its gates, conversation routing and onboarding, global callbacks, the
update inbox, conversation state, the per-user lock, category management and alias conflicts,
the category and budget conversation flows, the guided and compact expense flows, the
consume-once confirmation, expense persistence, constraints, cross-user integrity (all four
composite FKs), user isolation per repository, historical budget immutability, cascade and
non-deletion behaviour, user erasure, orphan removal, migration guardrails, timestamp
maintenance, allocation persistence, localization and matching option validation, the category
matcher corpus including ambiguity and the fuzzy default, the keyword-learning flow with its
conflict prompt, architecture and repository contract tests, the summary, statistics and
date-range history messages (asserted verbatim), the period queries against real PostgreSQL
(grouped sums, user isolation, the range boundary and a full keyset walk), the per-user inbound
throttle (budget, sliding window, one notice per window, independent budgets) and the outbound
429 retry policy (fallbacks and the cap). 708 tests, all green.

The restore drill is operational rather than unit-tested: it ran against the local stack on
2026-09-28 and its output and the corrupt-dump failure path are recorded in
[`BACKUPS.md`](BACKUPS.md#drill-log).

## 17. Backup strategy

Docker persistence is not a backup.

The implementation lives in `scripts/`, the runbook in [`BACKUPS.md`](BACKUPS.md).

- `scripts/backup.sh` (cron, nightly) dumps with
  `pg_dump --format=custom --no-owner --no-privileges` into `mybudget_pg_backups`, a volume
  separate from the data volume, then proves the dump is readable with `pg_restore --list`.
- The same dump is hardlinked into `weekly-<ISO week>` and `monthly-<YYYY-MM>` buckets, so
  long-term retention costs no extra space and survives pruning the daily file.
- Off-site copies are encrypted with `age` before leaving the VPS, when
  `BACKUP_OFFSITE_TARGET` and `BACKUP_AGE_RECIPIENT` are set; the private key stays off the
  machine.
- Retention: 7 daily, 4 weekly, 12 monthly (`BACKUP_RETENTION_DAYS`, `BACKUP_WEEKLY_RETENTION`,
  `BACKUP_MONTHLY_RETENTION`).
- **Verification**: every dump is listed; weekly, `scripts/verify-backup.sh` restores the newest
  dump into the throwaway `mybudget_restore_check` database with `--exit-on-error`, checks the
  data (migration history present, no non-positive amount, no missing calendar date, no user
  without a Telegram id), and drops it. Failures send a Telegram alert to the admin user
  directly through the API, so the alert works even when the application is down. A backup that
  has never been restored is not a backup.
- `scripts/restore.sh` is the break-glass restore into the live database: it stops the app,
  recreates the database owned by `mybudget_migrator`, restores as that role, and re-applies the
  least-privilege grants that `--no-privileges` deliberately omits.
- `scripts/install-backup-cron.sh` installs the nightly and weekly entries idempotently. The
  procedure is rehearsed, and the drill log is recorded in `BACKUPS.md`. **RPO ≤ 24 h, RTO ≤ 30
  min.**
- Point-in-time recovery via WAL archiving is a possible later addition, not MVP.

## 18. Risks and trade-offs

| Risk | Impact | Mitigation |
|---|---|---|
| A mis-parsed amount creates a wrong financial record | High | Layered parser, corpus plus property tests, normalised value always shown for confirmation, no silent rounding |
| Fuzzy matching silently mis-categorises | Medium | Off by default; unique distance-1 only; asking beats guessing |
| Telegram API or library breaking change | Medium | Pinned version; the SDK is isolated behind a sender abstraction and a Telegram-agnostic response model |
| Duplicate or out-of-order updates | Medium | Inbox table, per-user advisory lock, consume-once pending actions |
| Single VPS is a single point of failure | High | Verified off-site backups, documented restore, restart policies, healthchecks; the risk is accepted explicitly |
| Backup exists but cannot be restored | High | Automated weekly restore into a scratch database with sanity checks and an alert on failure |
| Secret leakage | High | Environment-only secrets, redaction, CI scans, ignore files |
| EF Core mis-models aggregate-created records | Medium | Store-generated keys for child records; documented in `AGENTS.md`; covered by tests |
| Globalization drift in `es-CO` output | Medium | Explicit `NumberFormatInfo`; Debian image with full ICU |
| Time-zone regressions in month boundaries | High | `ExpenseDate` is a calendar date; boundary tests; no hardcoded offsets |
| Scope creep into phase-9 features | High (schedule) | Phase gates; nothing starts without a real need |
| Renaming a category changes old report labels | Low | Accepted deliberately: snapshotting names would create a second source of truth |

## 19. Decisions taken against the original brief

| # | Brief | Decision |
|---|---|---|
| 1 | `IStringLocalizer` / ambient culture | Explicit-language message catalog. A bot has no request culture, and ambient state fails silently. |
| 2 | Prompt on every ambiguous amount | Parse deterministically; rely on the confirmation screen. Reserve prompts for genuinely dangerous input. |
| 3 | Optional lightweight fuzzy matching | Off by default; tightly bounded when enabled. |
| 4 | `ExpenseDate` field | A calendar date, not a timestamp. This is the most likely source of wrong-month bugs. |
| 5 | Onboarding currency step | Removed for a COP-only MVP; the schema stays currency-aware. |
| 6 | Charts in scope | Deferred to Phase 9; text visualisations first. |
| 7 | Store processed update ids | Inbox table with status plus a per-user advisory lock; claiming before processing would lose updates on failure. |
| 8 | Category aliases may be ambiguous | Allowed deliberately, with per-category uniqueness; ambiguity is resolved by asking. |
| 9 | "Category belongs to user" validation | Enforced by composite foreign keys in PostgreSQL, not only by application code. |
| 10 | `IsActive` plus hard/soft delete | Never hard delete; deactivate, and offer reactivation on name conflicts. |
| 11 | Personal single-user app | An allowlist, private chats only, and full multi-user isolation from day one. |
| 12 | Commands as the primary interface | Persistent reply keyboard, keeping the text field free for compact entry. |
| 13 | Markdown formatting | HTML parse mode, with values escaped by the presenter. |
| 14 | Migrations at startup | One-shot migrator container; the app never migrates in production. |
| 15 | Alpine runtime image | Debian, for ICU and tzdata. |
| 16 | Rich DDD aggregates | Pragmatic domain plus SQL read models; no aggregate loading just to sum expenses. |
| 17 | Callback data | Token-only payloads, drafts in the database, consumed once. |
| 18 | Uncategorised expenses | `category_id` is required, with a seeded, editable `📦 Otros` as the fallback. |
