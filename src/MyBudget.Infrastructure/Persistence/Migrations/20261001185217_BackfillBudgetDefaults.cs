using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyBudget.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BackfillBudgetDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Data repair, no schema change: an allocation that predates recurring budgets
            // becomes the recurring default of its category. See BudgetDefaultsBackfill.
            migrationBuilder.Sql(BudgetDefaultsBackfill.Sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately empty. The rows this migration inserts are indistinguishable from
            // defaults the user created afterwards, so removing them on a rollback would delete
            // real configuration. Rolling back is a redeploy, not a data deletion.
        }
    }
}
