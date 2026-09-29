using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MyBudget.Infrastructure.Persistence;

namespace MyBudget.Infrastructure.Tests;

/// <summary>
/// Guards the migration itself, not just the resulting behaviour.
/// Some constraints cannot be expressed by EF Core's model and are applied with raw SQL in
/// the InitialSchema migration; without this test a future migration could drop them and
/// the only symptom would be a data leak nobody notices.
/// </summary>
public sealed class MigrationTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task Migrating_an_up_to_date_database_is_a_no_op()
    {
        await using var context = CreateContext();
        var migrator = new DatabaseMigrator(context);

        await migrator.MigrateAsync();
        await migrator.MigrateAsync();

        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
        var available = context.Database.GetMigrations().ToList();

        // Migrating again changes neither the schema nor the history.
        applied.Should().BeEquivalentTo(available);
        applied.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task The_constraints_created_with_raw_sql_are_present()
    {
        await using var context = CreateContext();

        var names = await ReadConstraintNamesAsync(context);

        names.Should().BeEquivalentTo(
            "fk_budget_alerts_category_same_user",
            "fk_category_aliases_category_same_user",
            "fk_expenses_category_same_user",
            "fk_monthly_budget_categories_budget_same_user",
            "fk_monthly_budget_categories_category_same_user",
            "fk_recurring_expenses_category_same_user",
            "uq_categories_id_user",
            "uq_monthly_budgets_id_user");
    }

    [Fact]
    public async Task The_case_insensitive_category_name_index_is_present()
    {
        await using var context = CreateContext();
        await context.Database.OpenConnectionAsync();

        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            "SELECT indexdef FROM pg_indexes WHERE indexname = 'uq_categories_user_name'";

        var definition = (string?)await command.ExecuteScalarAsync();

        // PostgreSQL normalises the expression (it renders as lower(btrim((name)::text))),
        // so the assertion checks the properties that matter rather than the exact text.
        definition.Should().NotBeNull();
        definition.Should().Contain("UNIQUE");
        definition.Should().Contain("user_id");
        definition.Should().Contain("lower");
        definition.Should().Contain("btrim");
    }

    [Fact]
    public async Task The_category_foreign_keys_defer_nothing_and_cascade_nothing()
    {
        // ON DELETE NO ACTION without DEFERRABLE is load bearing: history cannot be
        // destroyed, and the error must surface immediately as a named constraint rather
        // than as a concurrency failure at commit time.
        await using var context = CreateContext();
        await context.Database.OpenConnectionAsync();

        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT pg_get_constraintdef(oid)
            FROM pg_constraint
            WHERE conname = 'fk_expenses_category_same_user'
            """;

        var definition = (string?)await command.ExecuteScalarAsync();

        definition.Should().NotBeNull();
        definition.Should().NotContain("DEFERRABLE");
        definition.Should().NotContain("CASCADE");
    }

    [Fact]
    public async Task The_recurring_category_foreign_key_also_defers_and_cascades_nothing()
    {
        await using var context = CreateContext();
        await context.Database.OpenConnectionAsync();

        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT pg_get_constraintdef(oid)
            FROM pg_constraint
            WHERE conname = 'fk_recurring_expenses_category_same_user'
            """;

        var definition = (string?)await command.ExecuteScalarAsync();

        definition.Should().NotBeNull();
        definition.Should().NotContain("DEFERRABLE");
        definition.Should().NotContain("CASCADE");
    }

    [Fact]
    public async Task The_budget_alert_category_foreign_key_also_defers_and_cascades_nothing()
    {
        await using var context = CreateContext();
        await context.Database.OpenConnectionAsync();

        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT pg_get_constraintdef(oid)
            FROM pg_constraint
            WHERE conname = 'fk_budget_alerts_category_same_user'
            """;

        var definition = (string?)await command.ExecuteScalarAsync();

        definition.Should().NotBeNull();
        definition.Should().NotContain("DEFERRABLE");
        definition.Should().NotContain("CASCADE");
    }

    private static async Task<IReadOnlyList<string>> ReadConstraintNamesAsync(MyBudgetDbContext context)
    {
        await context.Database.OpenConnectionAsync();

        var names = new List<string>();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT conname
            FROM pg_constraint
            WHERE conname LIKE 'fk_%_same_user'
               OR conname LIKE 'uq_%_id_user'
            ORDER BY conname
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
