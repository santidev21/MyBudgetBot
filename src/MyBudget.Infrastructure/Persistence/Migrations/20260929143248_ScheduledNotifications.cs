using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyBudget.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ScheduledNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "daily_reminder_enabled",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "reminder_deliveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    local_date = table.Column<DateOnly>(type: "date", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reminder_deliveries", x => x.id);
                    table.CheckConstraint("ck_reminder_deliveries_kind", "btrim(kind) <> ''");
                    table.ForeignKey(
                        name: "FK_reminder_deliveries_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "uq_reminder_deliveries_user_kind_date",
                table: "reminder_deliveries",
                columns: new[] { "user_id", "kind", "local_date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reminder_deliveries");

            migrationBuilder.DropColumn(
                name: "daily_reminder_enabled",
                table: "users");
        }
    }
}
