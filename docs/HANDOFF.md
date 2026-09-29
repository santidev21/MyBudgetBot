# Handoff — next task and backlog

The working context is [`AGENTS.md`](../AGENTS.md); the architecture is
[`TECHNICAL-DESIGN.md`](TECHNICAL-DESIGN.md). This file is only the next task's prompt and the
decisions already taken for what comes after.

## Done: scheduled summaries

The bot sends the previous month's closing on the user's first local day. The implementation:

- `monthly_closings` marker claimed with `ON CONFLICT DO NOTHING` (migration
  `20260929023821_MonthlyClosings`), so a restart cannot send the closing twice.
- `IMonthlyClosingService` in Application decides the local first day (`IUserLocalDate`), builds
  the closing from `IReportService` and returns only what it claimed.
- `MonthlyClosingScheduler` (`MyBudget.Telegram/Reporting`) is a `BackgroundService` with a
  `RunOnceAsync`, registered only with a bot token; it resolves the scoped service per pass.
- The closing message shows the total, the expense count, the comparison with the month before,
  the biggest categories with text bars and the overspending, plus a global callback button
  (`v1|closingcopy|YYYY|M`) that copies the closed month's budget into the new one. A month with
  no spending and no budget is skipped and its marker is left unspent.

Tests: application service (local-day decision, claim, previous month, empty-month skip), the
verbatim closing render, the scheduler pass, the copy callback, the store integration and the
exactly-once delivery against PostgreSQL. **801 tests green**, build with zero warnings,
`dotnet format` clean.

## Backlog with decisions already taken

When these are built, seed the default categories in this order (the user's choice) and use
them as examples when creating recurring rules:

1. Mensualidades
2. Ocio
3. Ropa
4. Otras
5. Regalos
6. Vivienda
7. Mercado
8. Deporte

- **Export CSV** of a date range's expenses. Import waits for later because of data integrity.
- **Onboarding with seed data.** On first use, offer to create those categories and leave one
  example recurring rule ready (for instance "Mensualidades"). Skippable: it never forces the
  user.
- **Edit a recurring rule** (amount and day) instead of deleting and recreating it.

