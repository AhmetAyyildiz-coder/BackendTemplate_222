using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendTemplate.Persistence.Migrations.Audit
{
    /// <inheritdoc />
    public partial class Initial_AuditDb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "data_change_audit_log",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OutboxMessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    TraceId = table.Column<string>(type: "text", nullable: false),
                    EntityType = table.Column<string>(type: "text", nullable: false),
                    EntityId = table.Column<string>(type: "text", nullable: false),
                    ChangeType = table.Column<string>(type: "text", nullable: false),
                    OldValuesJson = table.Column<string>(type: "text", nullable: true),
                    NewValuesJson = table.Column<string>(type: "text", nullable: true),
                    ChangedPropertiesJson = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    TimestampUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_change_audit_log", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_data_change_audit_log_EntityType_EntityId_TimestampUtc",
                table: "data_change_audit_log",
                columns: new[] { "EntityType", "EntityId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_data_change_audit_log_OutboxMessageId",
                table: "data_change_audit_log",
                column: "OutboxMessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_data_change_audit_log_TimestampUtc",
                table: "data_change_audit_log",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_data_change_audit_log_UserId_TimestampUtc",
                table: "data_change_audit_log",
                columns: new[] { "UserId", "TimestampUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_change_audit_log");
        }
    }
}
