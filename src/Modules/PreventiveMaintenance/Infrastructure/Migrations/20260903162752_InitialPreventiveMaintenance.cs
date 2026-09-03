using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cmms.Modules.PreventiveMaintenance.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialPreventiveMaintenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "preventive_maintenance");

            migrationBuilder.CreateTable(
                name: "maintenance_plans",
                schema: "preventive_maintenance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    recurrence_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    interval_days = table.Column<int>(type: "integer", nullable: false),
                    generation_lead_time_days = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    next_due_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    active_occurrence_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_plans", x => x.id);
                    table.UniqueConstraint("ak_maintenance_plans_site_id_id", x => new { x.site_id, x.id });
                    table.CheckConstraint("ck_maintenance_plans_interval_days", "interval_days > 0");
                    table.CheckConstraint("ck_maintenance_plans_lead_time", "generation_lead_time_days >= 0");
                    table.CheckConstraint("ck_maintenance_plans_recurrence_type", "recurrence_type IN ('Fixed', 'Floating')");
                    table.CheckConstraint("ck_maintenance_plans_status", "status IN ('Active', 'Paused')");
                });

            migrationBuilder.CreateTable(
                name: "maintenance_plan_occurrences",
                schema: "preventive_maintenance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scheduled_for_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    generated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_plan_occurrences", x => x.id);
                    table.ForeignKey(
                        name: "fk_maintenance_plan_occurrences_maintenance_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "preventive_maintenance",
                        principalTable: "maintenance_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_plan_occurrences_plan_id_scheduled_for_utc",
                schema: "preventive_maintenance",
                table: "maintenance_plan_occurrences",
                columns: new[] { "plan_id", "scheduled_for_utc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_plan_occurrences_site_id",
                schema: "preventive_maintenance",
                table: "maintenance_plan_occurrences",
                column: "site_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_plan_occurrences_work_order_id",
                schema: "preventive_maintenance",
                table: "maintenance_plan_occurrences",
                column: "work_order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_plans_site_id",
                schema: "preventive_maintenance",
                table: "maintenance_plans",
                column: "site_id");

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_plans_status_next_due_at_utc",
                schema: "preventive_maintenance",
                table: "maintenance_plans",
                columns: new[] { "status", "next_due_at_utc" });

            migrationBuilder.AddForeignKey(
                name: "fk_maintenance_plans_sites_site_id",
                schema: "preventive_maintenance",
                table: "maintenance_plans",
                column: "site_id",
                principalSchema: "identity_access",
                principalTable: "sites",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION preventive_maintenance.reject_site_id_change()
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

                CREATE TRIGGER maintenance_plans_site_id_immutable
                    BEFORE UPDATE OF site_id ON preventive_maintenance.maintenance_plans
                    FOR EACH ROW EXECUTE FUNCTION preventive_maintenance.reject_site_id_change();

                CREATE TRIGGER maintenance_plan_occurrences_site_id_immutable
                    BEFORE UPDATE OF site_id ON preventive_maintenance.maintenance_plan_occurrences
                    FOR EACH ROW EXECUTE FUNCTION preventive_maintenance.reject_site_id_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "maintenance_plan_occurrences",
                schema: "preventive_maintenance");

            migrationBuilder.DropTable(
                name: "maintenance_plans",
                schema: "preventive_maintenance");

            migrationBuilder.Sql("DROP FUNCTION preventive_maintenance.reject_site_id_change();");
        }
    }
}
