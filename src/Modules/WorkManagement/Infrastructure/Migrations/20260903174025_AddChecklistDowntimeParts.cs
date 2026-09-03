using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cmms.Modules.WorkManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChecklistDowntimeParts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "checklist_items",
                schema: "work_management",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_cycle = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    item_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    label = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    safety_critical = table.Column<bool>(type: "boolean", nullable: false),
                    numeric_min_value = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    numeric_max_value = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    numeric_unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    single_select_options_csv = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_resolved = table.Column<bool>(type: "boolean", nullable: false),
                    boolean_value = table.Column<bool>(type: "boolean", nullable: true),
                    numeric_value = table.Column<decimal>(type: "numeric(18,4)", nullable: true),
                    selected_option = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    note_text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    attachment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    numeric_out_of_tolerance = table.Column<bool>(type: "boolean", nullable: true),
                    resolved_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checklist_items", x => x.id);
                    table.CheckConstraint("ck_checklist_items_item_type", "item_type IN ('Boolean', 'Numeric', 'SingleSelect', 'PhotoRequired', 'Note')");
                    table.ForeignKey(
                        name: "fk_checklist_items_work_orders_work_order_id",
                        column: x => x.work_order_id,
                        principalSchema: "work_management",
                        principalTable: "work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "downtime_intervals",
                schema: "work_management",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asset_id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_cycle = table.Column<int>(type: "integer", nullable: false),
                    classification = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    started_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cause_category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    cause_mechanism = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_downtime_intervals", x => x.id);
                    table.CheckConstraint("ck_downtime_intervals_cause_category", "cause_category IS NULL OR cause_category IN ('Mechanical', 'Electrical', 'Hydraulic', 'Pneumatic', 'Instrumentation', 'Operational')");
                    table.CheckConstraint("ck_downtime_intervals_classification", "classification IN ('FullStop', 'PartialDerating')");
                    table.CheckConstraint("ck_downtime_intervals_ended_after_started", "ended_at_utc IS NULL OR ended_at_utc >= started_at_utc");
                    table.ForeignKey(
                        name: "fk_downtime_intervals_work_orders_work_order_id",
                        column: x => x.work_order_id,
                        principalSchema: "work_management",
                        principalTable: "work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "part_usages",
                schema: "work_management",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_cycle = table.Column<int>(type: "integer", nullable: false),
                    part_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    part_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    quantity = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,4)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_part_usages", x => x.id);
                    table.ForeignKey(
                        name: "fk_part_usages_work_orders_work_order_id",
                        column: x => x.work_order_id,
                        principalSchema: "work_management",
                        principalTable: "work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_checklist_items_work_order_id_execution_cycle",
                schema: "work_management",
                table: "checklist_items",
                columns: new[] { "work_order_id", "execution_cycle" });

            migrationBuilder.CreateIndex(
                name: "ix_downtime_intervals_asset_id",
                schema: "work_management",
                table: "downtime_intervals",
                column: "asset_id");

            migrationBuilder.CreateIndex(
                name: "ix_downtime_intervals_work_order_id_execution_cycle",
                schema: "work_management",
                table: "downtime_intervals",
                columns: new[] { "work_order_id", "execution_cycle" });

            migrationBuilder.CreateIndex(
                name: "ix_part_usages_work_order_id_execution_cycle",
                schema: "work_management",
                table: "part_usages",
                columns: new[] { "work_order_id", "execution_cycle" });

            migrationBuilder.CreateIndex(
                name: "ix_part_usages_work_order_id_idempotency_key",
                schema: "work_management",
                table: "part_usages",
                columns: new[] { "work_order_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            // docs/01 § "Downtime tracking", "Resolves QA finding O-02": a partial unique index on
            // "no two currently-open intervals" isn't enough — it doesn't stop two *already-closed*
            // FullStop intervals from overlapping in time, which would silently double-count
            // downtime in the MTTR/availability formulas. An exclusion constraint (via btree_gist,
            // for the scalar `=` on asset_id alongside the range `&&` overlap check) makes any two
            // FullStop intervals for the same asset mutually exclusive in time, full stop —
            // regardless of open/closed state. PartialDerating is deliberately NOT covered: docs/01
            // says those "are allowed to overlap by design ... summed, not deduplicated."
            migrationBuilder.Sql(
                """
                CREATE EXTENSION IF NOT EXISTS btree_gist;

                ALTER TABLE work_management.downtime_intervals
                    ADD CONSTRAINT ex_downtime_intervals_fullstop_no_overlap
                    EXCLUDE USING gist (
                        asset_id WITH =,
                        tstzrange(started_at_utc, ended_at_utc) WITH &&
                    )
                    WHERE (classification = 'FullStop');
                """);

            // docs/01: checklist_responses, downtime_intervals, and part_usages are explicitly
            // named alongside work_orders as tables carrying an immutable site_id — same pattern
            // as InitialWorkManagement's own trigger on work_orders, reusing that migration's
            // already-created work_management.reject_site_id_change() function (schema-scoped, not
            // table-specific).
            migrationBuilder.AddForeignKey(
                name: "fk_checklist_items_sites_site_id",
                schema: "work_management",
                table: "checklist_items",
                column: "site_id",
                principalSchema: "identity_access",
                principalTable: "sites",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_downtime_intervals_sites_site_id",
                schema: "work_management",
                table: "downtime_intervals",
                column: "site_id",
                principalSchema: "identity_access",
                principalTable: "sites",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_part_usages_sites_site_id",
                schema: "work_management",
                table: "part_usages",
                column: "site_id",
                principalSchema: "identity_access",
                principalTable: "sites",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER checklist_items_site_id_immutable
                    BEFORE UPDATE OF site_id ON work_management.checklist_items
                    FOR EACH ROW EXECUTE FUNCTION work_management.reject_site_id_change();

                CREATE TRIGGER downtime_intervals_site_id_immutable
                    BEFORE UPDATE OF site_id ON work_management.downtime_intervals
                    FOR EACH ROW EXECUTE FUNCTION work_management.reject_site_id_change();

                CREATE TRIGGER part_usages_site_id_immutable
                    BEFORE UPDATE OF site_id ON work_management.part_usages
                    FOR EACH ROW EXECUTE FUNCTION work_management.reject_site_id_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "checklist_items",
                schema: "work_management");

            migrationBuilder.DropTable(
                name: "downtime_intervals",
                schema: "work_management");

            migrationBuilder.DropTable(
                name: "part_usages",
                schema: "work_management");
        }
    }
}
