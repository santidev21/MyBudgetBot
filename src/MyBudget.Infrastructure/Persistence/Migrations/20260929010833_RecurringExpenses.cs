using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyBudget.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecurringExpenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "recurring_expenses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    day_of_month = table.Column<short>(type: "smallint", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_generated_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_recurring_expenses", x => x.id);
                    table.CheckConstraint("ck_recurring_expenses_amount", "amount > 0 AND amount <= 999999999999");
                    table.CheckConstraint("ck_recurring_expenses_day", "day_of_month BETWEEN 1 AND 31");
                    table.CheckConstraint("ck_recurring_expenses_description", "description IS NULL OR char_length(description) <= 500");
                    table.CheckConstraint("ck_recurring_expenses_period", "end_date IS NULL OR end_date >= start_date");
                    table.ForeignKey(
                        name: "FK_recurring_expenses_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_recurring_expenses_user_active",
                table: "recurring_expenses",
                columns: new[] { "user_id", "is_active" });

            // A recurring rule cannot reference another user's category even if application
            // code or a manual statement is wrong. The composite key target was created by
            // the InitialSchema migration: uq_categories_id_user.
            //
            // ON DELETE NO ACTION is deliberate, like the other composite foreign keys:
            // PostgreSQL reports a named violation instead of silently orphaning the rule.
            // IUserDataEraser deletes recurring rules before categories.
            migrationBuilder.Sql(
                "ALTER TABLE recurring_expenses " +
                "ADD CONSTRAINT fk_recurring_expenses_category_same_user " +
                "FOREIGN KEY (category_id, user_id) REFERENCES categories (id, user_id) " +
                "ON DELETE NO ACTION;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE recurring_expenses " +
                "DROP CONSTRAINT IF EXISTS fk_recurring_expenses_category_same_user;");

            migrationBuilder.DropTable(
                name: "recurring_expenses");
        }
    }
}
