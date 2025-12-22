using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendTemplate.Persistence.Migrations.App
{
    /// <inheritdoc />
    public partial class Initial_AppDb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_outbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LockedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LockOwner = table.Column<string>(type: "text", nullable: true),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_outbox", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_outbox_CreatedAtUtc",
                table: "audit_outbox",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_audit_outbox_LockedUntilUtc",
                table: "audit_outbox",
                column: "LockedUntilUtc");

            migrationBuilder.CreateIndex(
                name: "IX_audit_outbox_ProcessedAtUtc",
                table: "audit_outbox",
                column: "ProcessedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_outbox");
        }
    }
}
