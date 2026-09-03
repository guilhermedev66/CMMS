using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cmms.Modules.WorkManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialWorkManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "work_management");

            migrationBuilder.CreateTable(
                name: "work_orders",
                schema: "work_management",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    assignee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    assigned_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    priority = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    wrench_start_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancelled_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    reopen_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    execution_cycle = table.Column<int>(type: "integer", nullable: false),
                    source_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_orders", x => x.id);
                    table.UniqueConstraint("ak_work_orders_site_id_id", x => new { x.site_id, x.id });
                    table.CheckConstraint("ck_work_orders_priority", "priority IN ('P1', 'P2', 'P3', 'P4')");
                    table.CheckConstraint("ck_work_orders_status", "status IN ('Draft', 'Open', 'Scheduled', 'InProgress', 'Completed', 'Closed', 'Cancelled')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_assignee_id",
                schema: "work_management",
                table: "work_orders",
                column: "assignee_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_site_id",
                schema: "work_management",
                table: "work_orders",
                column: "site_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_site_id_status",
                schema: "work_management",
                table: "work_orders",
                columns: new[] { "site_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_source_request_id",
                schema: "work_management",
                table: "work_orders",
                column: "source_request_id",
                unique: true,
                filter: "source_request_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_work_orders_sites_site_id",
                schema: "work_management",
                table: "work_orders",
                column: "site_id",
                principalSchema: "identity_access",
                principalTable: "sites",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION work_management.reject_site_id_change()
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

                CREATE TRIGGER work_orders_site_id_immutable
                    BEFORE UPDATE OF site_id ON work_management.work_orders
                    FOR EACH ROW EXECUTE FUNCTION work_management.reject_site_id_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "work_orders",
                schema: "work_management");

            migrationBuilder.Sql("DROP FUNCTION work_management.reject_site_id_change();");
        }
    }
}
