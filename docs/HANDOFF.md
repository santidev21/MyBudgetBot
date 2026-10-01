# Handoff — next task and backlog

The working context is [`AGENTS.md`](../AGENTS.md); the architecture is
[`TECHNICAL-DESIGN.md`](TECHNICAL-DESIGN.md). This file holds the project's current status,
the next task's prompt and the decisions already taken for what comes after.

## Project status

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
into each category's movements. The category chart measures each bar as its share of the month
and draws the category name; the breakdown has a way back. **848 tests green**, build with zero
warnings, `dotnet format` clean.

## Done in this round (all verified, 848 tests green)

**Scheduled-notification foundation**

- `IUserLocalDate.LocalNow(timeZoneId)` returns the clock in the user's zone, so a schedule can
  compare the time of day. `Today`/`FromUtc` are unchanged.
- Migration `20260929143248_ScheduledNotifications`: `users.daily_reminder_enabled`
  (`boolean NOT NULL DEFAULT true`) and `reminder_deliveries (user_id, kind, local_date)`
  unique. `IReminderDeliveryStore` + `ReminderDeliveryStore` claim with `ON CONFLICT DO NOTHING`,
  the same pattern as `monthly_closings`.

**Settings (⚙️ Configuración)**

- `SettingsConversation` (`settings`) mapped in `ConversationRouter.StartMenuActionAsync`.
  It has the daily-reminder on/off toggle and "🗑️ Borrar mis datos" behind a double
  confirmation that calls `IUserDataEraser`. `FeatureNotReady` is no longer shown for Settings.
- Erasing the user row needed two fixes: `UserDataEraser` now reuses the turn's ambient
  transaction (`UserWorkLock` already opens one), and `ConversationTurn.UserRemoved` makes the
  dispatcher settle the inbox with a `null` owner instead of a deleted `user_id`.

**Recurring budget + one-month overrides**

- `BudgetDefault` (`budget_defaults`), unique per `(user_id, category_id)`, with
  `effective_from` so it never rewrites a month before it existed. Migration `20260929144513_
  BudgetDefaults`, including the composite FK `(category_id, user_id) -> categories`.
- `EffectiveBudget.Merge`: the month's own rows win, the default fills the rest from its month
  on. Used by `BudgetService.GetMonthAsync` and `ReportService.GetMonthlySummaryAsync`.
- `IBudgetService.SetAllocationAsync(..., BudgetScope scope, ...)`: `Month` writes the override,
  `AllMonths` upserts the default and clears that month's override. First assignment a user ever
  makes is `AllMonths` automatically (`HasDefaultsAsync` decides); later ones ask with two
  buttons.
- The "copy previous month" feature was retired everywhere: domain `CopyAllocationsFrom`,
  `IBudgetService.CopyPreviousMonthAsync`, `ClosingCopyHandler`, `ClosingCopyCallback`, the
  budget-screen button and the closing-message button, and their message keys.
- Migration `20261001185217_BackfillBudgetDefaults` (SQL in `BudgetDefaultsBackfill`) repairs the
  users who predate recurring budgets: it turns the latest allocation of each category into a
  `budget_defaults` row, so a later month inherits it without a copy. It is idempotent and never
  invents a budget for a month before the allocation.
- `IBudgetService.PromoteMonthToDefaultsAsync` and the budget screen's "🔁 Aplicar todos los
  meses" button turn the current month's overrides into the recurring default (keeping an
  existing default's `effective_from`). Covered by `BudgetServiceTests`, `BudgetInheritanceTests`
  and `CategoriesConversationBudgetTests`.

**Summary and per-category breakdown**

- `SummaryConversation` adds "Te quedan … este mes." / "Te pasaste por … este mes." when the
  month has a budget, plus a "📂 Por categoría" button.
- `CategoryDetailConversation` (`category-detail`): pick a category with spending for the current
  month, see every movement (date, description, amount) and the total/count. Tapping a movement
  hands it to the expenses flow, which owns the detail, edit and delete.
- New routing primitive: `ConversationTurn.HandoffConversation` / `HandoffPayload` and
  `IHandoffConversation.StartWithAsync`. `ConversationRouter.ResolveHandoffAsync` starts the
  target flow. `ExpensesConversation` implements it to open one expense.
- `IExpenseReadRepository.ListPageAsync` and `IReportService.GetHistoryAsync` accept an optional
  `categoryId` (implemented with a boolean flag, not a nullable SQL parameter).

**Statistics chart (category)**

- Each row of the category chart now carries the number, the category name and
  `$gasto ($share %)` on one line, with the bar underneath: the bar is that share of the month's
  total (`SpendingChartRenderer.HorizontalBars(entries, total)`), so 3 % is 3 % of the row
  instead of a fraction of the longest bar, which is what made a "3 %" bar look halfway. The
  caption keeps a numbered legend with the icon and the budget
  (`Statistics.ChartCategoryLegend*`), read from `GetMonthlySummaryAsync`; the daily chart is
  unchanged.
- `BitmapFont` gained `A-Z`, `a-z` and the punctuation a name needs, and folds accents before
  drawing (`á` → `a`) because a 5x7 glyph cannot hold a diacritic. `BitmapFont.Truncate` cuts a
  name that does not fit and marks the cut with `..`. Icons and other scripts still leave a
  blank, which is why the exact name stays in the caption too.
- The breakdown chooser ("📂 ¿Qué categoría quieres revisar?") ends in an "↩️ Volver" row that
  hands off to the summary (`CategoryDetailConversation.BackToSummaryCallback`), so the flow has
  an exit besides the persistent menu. It is present even when the month has no movements.

**Daily reminder at 21:00 local**

- `IDailyReminderService` / `DailyReminderService` (`Application/Reminders`): for each user, skip
  if `DailyReminderEnabled` is off, skip before the user's local 21:00 (so a bot back the next
  morning does not remind about yesterday), skip if an expense exists for the local day
  (`IExpenseReadRepository.ExistsOnAsync`), then claim `("daily", local day)` before returning.
- `DailyReminderNotifier` and `ScheduledNotificationsScheduler` (`Telegram/Reminders`), registered
  only with a bot token. The scheduler ticks every minute and resolves the scoped services per
  pass. Tests: `DailyReminderServiceTests`, `ScheduledNotificationsSchedulerTests`,
  `DailyReminderDeliveryTests` (exactly-once against PostgreSQL).

**Monthly closing at 23:59 on the last local day**

- `MonthlyClosingService.PrepareDueAsync` now resolves the period per user: on the last local day
  from 23:59 it closes that month, and on the first local day it closes the previous month as the
  fallback. Both paths yield the same `closedPeriod`, so the `(user_id, year, month)` marker keeps
  it exactly-once.
- The closing moved into `ScheduledNotificationsScheduler` (the every-minute ticker), so
  `MonthlyClosingScheduler` was deleted. Tests: `MonthlyClosingServiceTests` (last day, before
  23:59, day-1 fallback), `ScheduledNotificationsSchedulerTests`, `MonthlyClosingDeliveryTests`.

## Backlog

- CSV export of a date range's expenses.
- Seed categories on onboarding.
- Edit a recurring rule instead of delete-and-recreate.
