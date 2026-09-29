# Handoff — next task and backlog

The working context is [`AGENTS.md`](../AGENTS.md); the architecture is
[`TECHNICAL-DESIGN.md`](TECHNICAL-DESIGN.md). This file is only the next task's prompt and the
decisions already taken for what comes after.

## Done in this round (all verified, 828 tests green)

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

- The category chart now numbers each bar and labels it `$gastado/$presupuesto uso%`, and the
  caption carries a numbered legend with the category names (`Statistics.ChartCategoryLegend*`).
  It reads `GetMonthlySummaryAsync` for the per-category budget; the daily chart is unchanged.
- `BitmapFont` gained `$` and `/` glyphs so the money and the separator are legible; category
  names still stay in the caption because accented letters are not in the font.

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
