using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyBudget.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MonthlyClosings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "monthly_closings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    year = table.Column<short>(type: "smallint", nullable: false),
                    month = table.Column<short>(type: "smallint", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monthly_closings", x => x.id);
                    table.CheckConstraint("ck_monthly_closings_month", "month BETWEEN 1 AND 12");
                    table.CheckConstraint("ck_monthly_closings_year", "year BETWEEN 2000 AND 2100");
                    table.ForeignKey(
                        name: "FK_monthly_closings_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "uq_monthly_closings_user_period",
                table: "monthly_closings",
                columns: new[] { "user_id", "year", "month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "monthly_closings");
        }
    }
}
