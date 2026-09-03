using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cmms.Modules.MaintenanceRequests.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialMaintenanceRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "maintenance_requests");

            migrationBuilder.CreateTable(
                name: "requests",
                schema: "maintenance_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    priority = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    converted_work_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rejected_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requests", x => x.id);
                    table.UniqueConstraint("ak_requests_site_id_id", x => new { x.site_id, x.id });
                    table.CheckConstraint("ck_requests_asset_or_location", "asset_id IS NOT NULL OR location_id IS NOT NULL");
                    table.CheckConstraint("ck_requests_priority", "priority IN ('P1', 'P2', 'P3', 'P4')");
                    table.CheckConstraint("ck_requests_status", "status IN ('New', 'Converted', 'Rejected', 'Cancelled')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_requests_converted_work_order_id",
                schema: "maintenance_requests",
                table: "requests",
                column: "converted_work_order_id",
                unique: true,
                filter: "converted_work_order_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_requests_created_by_user_id",
                schema: "maintenance_requests",
                table: "requests",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_requests_site_id",
                schema: "maintenance_requests",
                table: "requests",
                column: "site_id");

            migrationBuilder.CreateIndex(
                name: "ix_requests_status",
                schema: "maintenance_requests",
                table: "requests",
                column: "status");

            migrationBuilder.AddForeignKey(
                name: "fk_requests_sites_site_id",
                schema: "maintenance_requests",
                table: "requests",
                column: "site_id",
                principalSchema: "identity_access",
                principalTable: "sites",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION maintenance_requests.reject_site_id_change()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    IF NEW.site_id IS DISTINCT FROM OLD.site_id THEN
                        RAISE EXCEPTION 'site_id is immutable after creation'
                            USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $function$;

                CREATE TRIGGER requests_site_id_immutable
                    BEFORE UPDATE OF site_id ON maintenance_requests.requests
                    FOR EACH ROW EXECUTE FUNCTION maintenance_requests.reject_site_id_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "requests",
                schema: "maintenance_requests");

            migrationBuilder.Sql("DROP FUNCTION maintenance_requests.reject_site_id_change();");
        }
    }
}
