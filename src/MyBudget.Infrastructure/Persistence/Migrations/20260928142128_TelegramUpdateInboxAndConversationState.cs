using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyBudget.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TelegramUpdateInboxAndConversationState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "conversation_states",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_id = table.Column<long>(type: "bigint", nullable: false),
                    conversation = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    state = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_conversation_states", x => x.user_id);
                    table.CheckConstraint("ck_conversation_states_non_empty", "btrim(conversation) <> '' AND btrim(state) <> ''");
                    table.ForeignKey(
                        name: "FK_conversation_states_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "telegram_updates",
                columns: table => new
                {
                    update_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telegram_updates", x => x.update_id);
                    table.CheckConstraint("ck_telegram_updates_status", "status IN ('received','processed','failed','ignored')");
                    table.ForeignKey(
                        name: "FK_telegram_updates_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_telegram_updates_received_at",
                table: "telegram_updates",
                column: "received_at");

            migrationBuilder.CreateIndex(
                name: "IX_telegram_updates_user_id",
                table: "telegram_updates",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "conversation_states");

            migrationBuilder.DropTable(
                name: "telegram_updates");
        }
    }
}
