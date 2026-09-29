using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyBudget.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BudgetDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "budget_defaults",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from_year = table.Column<short>(type: "smallint", nullable: false),
                    effective_from_month = table.Column<short>(type: "smallint", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_budget_defaults", x => x.id);
                    table.CheckConstraint("ck_budget_defaults_amount", "amount >= 0");
                    table.CheckConstraint("ck_budget_defaults_month", "effective_from_month BETWEEN 1 AND 12");
                    table.CheckConstraint("ck_budget_defaults_year", "effective_from_year BETWEEN 2000 AND 2100");
                    table.ForeignKey(
                        name: "FK_budget_defaults_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "uq_budget_defaults_user_category",
                table: "budget_defaults",
                columns: new[] { "user_id", "category_id" },
                unique: true);

            // A recurring allocation cannot reference another user's category even if application
            // code or a manual statement is wrong. The composite key target was created by the
            // InitialSchema migration: uq_categories_id_user.
            migrationBuilder.Sql(
                "ALTER TABLE budget_defaults " +
                "ADD CONSTRAINT fk_budget_defaults_category_same_user " +
                "FOREIGN KEY (category_id, user_id) REFERENCES categories (id, user_id) " +
                "ON DELETE NO ACTION;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE budget_defaults " +
                "DROP CONSTRAINT IF EXISTS fk_budget_defaults_category_same_user;");

            migrationBuilder.DropTable(
                name: "budget_defaults");
        }
    }
}
