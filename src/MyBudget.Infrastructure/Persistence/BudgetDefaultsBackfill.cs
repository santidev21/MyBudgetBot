namespace MyBudget.Infrastructure.Persistence;

/// <summary>
/// The one-off data repair that makes an existing per-month budget recur.
/// <para>
/// Before recurring budgets existed, every assignment was written as a
/// <c>monthly_budget_categories</c> row, so a user who configured September had nothing to
/// inherit in October. This turns the <em>latest</em> allocation of each category into a
/// <c>budget_defaults</c> row (effective from the month it was made) so later months inherit
/// it. A category that was never assigned gets no default, and a month before the newest
/// allocation is never granted one, so nothing that was already recorded is rewritten.
/// <c>ON CONFLICT DO NOTHING</c> keeps a re-run from duplicating a default that already exists.
/// </para>
/// <para>
/// It lives next to the migration rather than inline so the integration test can run the exact
/// same statement against a seeded database.
/// </para>
/// </summary>
internal static class BudgetDefaultsBackfill
{
    public const string Sql = """
        INSERT INTO budget_defaults (
            id, user_id, category_id,
            effective_from_year, effective_from_month,
            amount, created_at, updated_at)
        SELECT
            gen_random_uuid(),
            latest.user_id,
            latest.category_id,
            latest.year,
            latest.month,
            latest.amount,
            now(),
            now()
        FROM (
            SELECT DISTINCT ON (allocation.user_id, allocation.category_id)
                allocation.user_id,
                allocation.category_id,
                budget.year,
                budget.month,
                allocation.amount
            FROM monthly_budget_categories AS allocation
            JOIN monthly_budgets AS budget ON budget.id = allocation.monthly_budget_id
            ORDER BY
                allocation.user_id,
                allocation.category_id,
                budget.year DESC,
                budget.month DESC
        ) AS latest
        ON CONFLICT (user_id, category_id) DO NOTHING;
        """;
}
