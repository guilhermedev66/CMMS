using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cmms.Modules.Audit.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // TODO(DB-role hardening, deferred): per docs/02-security-and-invariants.md, the
            // application's *runtime* DB role should ultimately have INSERT/SELECT only on
            // audit.audit_events (no UPDATE/DELETE) so the ordinary app code path cannot alter
            // or erase audit rows. This migration runs (and the API connects) as a single
            // owning/migrating role for now, so that REVOKE isn't applied here — see the longer
            // note on AuditDbContext. Not applied blindly because doing so would also need a
            // second, narrower runtime connection role wired through configuration, which is a
            // deployment/ops change out of scope for this slice.
            migrationBuilder.EnsureSchema(
                name: "audit");

            migrationBuilder.CreateTable(
                name: "audit_events",
                schema: "audit",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    resource_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    before_json = table.Column<string>(type: "jsonb", nullable: true),
                    after_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_occurred_at_utc",
                schema: "audit",
                table: "audit_events",
                column: "occurred_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_resource_type_resource_id",
                schema: "audit",
                table: "audit_events",
                columns: new[] { "resource_type", "resource_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_site_id",
                schema: "audit",
                table: "audit_events",
                column: "site_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events",
                schema: "audit");
        }
    }
}
