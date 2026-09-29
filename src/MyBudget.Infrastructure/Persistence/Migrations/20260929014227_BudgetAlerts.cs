using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyBudget.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BudgetAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "budget_alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<short>(type: "smallint", nullable: false),
                    month = table.Column<short>(type: "smallint", nullable: false),
                    threshold = table.Column<short>(type: "smallint", nullable: false),
                    notified_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_budget_alerts", x => x.id);
                    table.CheckConstraint("ck_budget_alerts_month", "month BETWEEN 1 AND 12");
                    table.CheckConstraint("ck_budget_alerts_threshold", "threshold IN (80, 100)");
                    table.CheckConstraint("ck_budget_alerts_year", "year BETWEEN 2000 AND 2100");
                    table.ForeignKey(
                        name: "FK_budget_alerts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "uq_budget_alerts_user_category_period_threshold",
                table: "budget_alerts",
                columns: new[] { "user_id", "category_id", "year", "month", "threshold" },
                unique: true);

            // A budget alert cannot reference another user's category even if application code
            // or a manual statement is wrong. The composite key target was created by the
            // InitialSchema migration: uq_categories_id_user.
            migrationBuilder.Sql(
                "ALTER TABLE budget_alerts " +
                "ADD CONSTRAINT fk_budget_alerts_category_same_user " +
                "FOREIGN KEY (category_id, user_id) REFERENCES categories (id, user_id) " +
                "ON DELETE NO ACTION;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE budget_alerts " +
                "DROP CONSTRAINT IF EXISTS fk_budget_alerts_category_same_user;");

            migrationBuilder.DropTable(
                name: "budget_alerts");
        }
    }
}
