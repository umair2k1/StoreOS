using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _002_Settings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "settings",
                schema: "public",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    key = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.ForeignKey(
                        name: "FK_settings_branches_branch_id",
                        column: x => x.branch_id,
                        principalSchema: "public",
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_settings_branch_id",
                schema: "public",
                table: "settings",
                column: "branch_id");

            migrationBuilder.Sql(@"
                ALTER TABLE settings ADD COLUMN branch_id_key uuid
                    GENERATED ALWAYS AS (COALESCE(branch_id, '00000000-0000-0000-0000-000000000000'::uuid)) STORED;
                ALTER TABLE settings ADD CONSTRAINT pk_settings PRIMARY KEY (tenant_id, branch_id_key, key);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE settings DROP CONSTRAINT IF EXISTS pk_settings;
                ALTER TABLE settings DROP COLUMN IF EXISTS branch_id_key;
            ");

            migrationBuilder.DropTable(
                name: "settings",
                schema: "public");
        }
    }
}
