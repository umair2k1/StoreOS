using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _004_RefreshTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_revoked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    replaced_by_token_id = table.Column<Guid>(type: "uuid", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "FK_refresh_tokens_refresh_tokens_replaced_by_token_id",
                        column: x => x.replaced_by_token_id,
                        principalSchema: "public",
                        principalTable: "refresh_tokens",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "public",
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_family",
                schema: "public",
                table: "refresh_tokens",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_lookup",
                schema: "public",
                table: "refresh_tokens",
                columns: new[] { "user_id", "token_hash" });

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_replaced_by_token_id",
                schema: "public",
                table: "refresh_tokens",
                column: "replaced_by_token_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "refresh_tokens",
                schema: "public");
        }
    }
}
