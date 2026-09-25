using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyBudget.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    telegram_user_id = table.Column<long>(type: "bigint", nullable: false),
                    username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    language = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                    table.CheckConstraint("ck_users_currency", "currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_users_language", "language ~ '^[a-z]{2}(-[A-Z]{2})?$'");
                });

            migrationBuilder.CreateTable(
                name: "categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    icon = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_categories", x => x.id);
                    table.CheckConstraint("ck_categories_icon", "char_length(icon) <= 16");
                    table.CheckConstraint("ck_categories_name", "btrim(name) <> '' AND char_length(name) <= 60");
                    table.ForeignKey(
                        name: "FK_categories_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "expenses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    expense_date = table.Column<DateOnly>(type: "date", nullable: false),
                    categorization_source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expenses", x => x.id);
                    table.CheckConstraint("ck_expenses_amount", "amount > 0 AND amount <= 999999999999");
                    table.CheckConstraint("ck_expenses_description", "description IS NULL OR char_length(description) <= 500");
                    table.CheckConstraint("ck_expenses_source", "categorization_source IN ('manual','matched','ambiguous','learned')");
                    table.ForeignKey(
                        name: "FK_expenses_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "monthly_budgets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<short>(type: "smallint", nullable: false),
                    month = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monthly_budgets", x => x.id);
                    table.CheckConstraint("ck_monthly_budgets_month", "month BETWEEN 1 AND 12");
                    table.CheckConstraint("ck_monthly_budgets_year", "year BETWEEN 2000 AND 2100");
                    table.ForeignKey(
                        name: "FK_monthly_budgets_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "category_aliases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alias = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    normalized_alias = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_category_aliases", x => x.id);
                    table.CheckConstraint("ck_category_aliases_alias", "btrim(alias) <> '' AND char_length(alias) <= 60");
                    table.ForeignKey(
                        name: "FK_category_aliases_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "monthly_budget_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    monthly_budget_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monthly_budget_categories", x => x.id);
                    table.CheckConstraint("ck_monthly_budget_categories_amount", "amount >= 0");
                    table.ForeignKey(
                        name: "FK_monthly_budget_categories_monthly_budgets_monthly_budget_id",
                        column: x => x.monthly_budget_id,
                        principalTable: "monthly_budgets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_categories_user_id",
                table: "categories",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_category_aliases_user_normalized",
                table: "category_aliases",
                columns: new[] { "user_id", "normalized_alias" });

            migrationBuilder.CreateIndex(
                name: "uq_category_aliases_category_normalized",
                table: "category_aliases",
                columns: new[] { "category_id", "normalized_alias" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expenses_user_category_date",
                table: "expenses",
                columns: new[] { "user_id", "category_id", "expense_date" });

            migrationBuilder.CreateIndex(
                name: "ix_expenses_user_date",
                table: "expenses",
                columns: new[] { "user_id", "expense_date", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "uq_monthly_budget_categories_budget_category",
                table: "monthly_budget_categories",
                columns: new[] { "monthly_budget_id", "category_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_monthly_budgets_period",
                table: "monthly_budgets",
                columns: new[] { "user_id", "year", "month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_users_telegram_user_id",
                table: "users",
                column: "telegram_user_id",
                unique: true);

            // -------------------------------------------------------------------------
            // Cross-user integrity + case-insensitive category names.
            //
            // EF Core cannot express a foreign key that targets a non-primary unique
            // key, so the constraints below are added as raw SQL on purpose.
            //
            // DO NOT REMOVE. These are what make it structurally impossible for one
            // user's expense, allocation or alias to reference another user's data,
            // even if application code or a manual statement is wrong.
            // -------------------------------------------------------------------------

            // Composite key targets referenced by the composite foreign keys below.
            migrationBuilder.Sql(
                "ALTER TABLE categories ADD CONSTRAINT uq_categories_id_user UNIQUE (id, user_id);");
            migrationBuilder.Sql(
                "ALTER TABLE monthly_budgets ADD CONSTRAINT uq_monthly_budgets_id_user UNIQUE (id, user_id);");

            // A user cannot own two categories whose names differ only by case/whitespace.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX uq_categories_user_name " +
                "ON categories (user_id, lower(btrim(name)));");

            // Composite foreign keys: the owner is part of the reference.
            //
            // ON DELETE NO ACTION is deliberate: history cannot be destroyed. Deleting a
            // category that still has expenses or a funded allocation fails immediately
            // with a named constraint, which keeps diagnostics precise.
            //
            // Consequence: PostgreSQL does not offer a reliable one-statement cascade for
            // deleting a user across these constraints (cascade ordering between them is
            // not something we depend on). Erasing a user is therefore an explicit,
            // ordered, transactional operation performed by IUserDataEraser.
            //
            // Do not make these DEFERRABLE. With EF's single-command autocommit saves the
            // violation then surfaces at implicit commit and EF reports it as a concurrency
            // failure instead of a foreign key violation.
            migrationBuilder.Sql(
                "ALTER TABLE category_aliases ADD CONSTRAINT fk_category_aliases_category_same_user " +
                "FOREIGN KEY (category_id, user_id) REFERENCES categories (id, user_id) ON DELETE CASCADE;");

            migrationBuilder.Sql(
                "ALTER TABLE monthly_budget_categories " +
                "ADD CONSTRAINT fk_monthly_budget_categories_budget_same_user " +
                "FOREIGN KEY (monthly_budget_id, user_id) REFERENCES monthly_budgets (id, user_id) ON DELETE CASCADE;");

            migrationBuilder.Sql(
                "ALTER TABLE monthly_budget_categories " +
                "ADD CONSTRAINT fk_monthly_budget_categories_category_same_user " +
                "FOREIGN KEY (category_id, user_id) REFERENCES categories (id, user_id) " +
                "ON DELETE NO ACTION;");

            migrationBuilder.Sql(
                "ALTER TABLE expenses ADD CONSTRAINT fk_expenses_category_same_user " +
                "FOREIGN KEY (category_id, user_id) REFERENCES categories (id, user_id) " +
                "ON DELETE NO ACTION;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE expenses DROP CONSTRAINT IF EXISTS fk_expenses_category_same_user;");
            migrationBuilder.Sql(
                "ALTER TABLE monthly_budget_categories " +
                "DROP CONSTRAINT IF EXISTS fk_monthly_budget_categories_category_same_user;");
            migrationBuilder.Sql(
                "ALTER TABLE monthly_budget_categories " +
                "DROP CONSTRAINT IF EXISTS fk_monthly_budget_categories_budget_same_user;");
            migrationBuilder.Sql(
                "ALTER TABLE category_aliases DROP CONSTRAINT IF EXISTS fk_category_aliases_category_same_user;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS uq_categories_user_name;");
            migrationBuilder.Sql("ALTER TABLE monthly_budgets DROP CONSTRAINT IF EXISTS uq_monthly_budgets_id_user;");
            migrationBuilder.Sql("ALTER TABLE categories DROP CONSTRAINT IF EXISTS uq_categories_id_user;");

            migrationBuilder.DropTable(
                name: "category_aliases");

            migrationBuilder.DropTable(
                name: "expenses");

            migrationBuilder.DropTable(
                name: "monthly_budget_categories");

            migrationBuilder.DropTable(
                name: "categories");

            migrationBuilder.DropTable(
                name: "monthly_budgets");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
