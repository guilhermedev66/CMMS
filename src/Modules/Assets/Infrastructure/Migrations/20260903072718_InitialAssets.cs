using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cmms.Modules.Assets.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "assets");

            migrationBuilder.CreateTable(
                name: "locations",
                schema: "assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    parent_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_locations", x => x.id);
                    table.UniqueConstraint("ak_locations_site_id_id", x => new { x.site_id, x.id });
                    table.CheckConstraint("ck_locations_code_normalized", "code = upper(btrim(code))");
                    table.CheckConstraint("ck_locations_not_own_parent", "parent_location_id IS NULL OR parent_location_id <> id");
                    table.ForeignKey(
                        name: "fk_locations_locations_site_id_parent_location_id",
                        columns: x => new { x.site_id, x.parent_location_id },
                        principalSchema: "assets",
                        principalTable: "locations",
                        principalColumns: new[] { "site_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "assets",
                schema: "assets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    normalized_tag = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    manufacturer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    serial_number = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    criticality = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    current_location_id = table.Column<Guid>(type: "uuid", nullable: true),
                    parent_asset_id = table.Column<Guid>(type: "uuid", nullable: true),
                    qr_locator = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assets", x => x.id);
                    table.UniqueConstraint("ak_assets_site_id_id", x => new { x.site_id, x.id });
                    table.CheckConstraint("ck_assets_criticality", "criticality IN ('A', 'B', 'C')");
                    table.CheckConstraint("ck_assets_normalized_tag", "normalized_tag = upper(btrim(tag))");
                    table.CheckConstraint("ck_assets_not_own_parent", "parent_asset_id IS NULL OR parent_asset_id <> id");
                    table.CheckConstraint("ck_assets_status", "status IN ('InService', 'OutOfService', 'Retired')");
                    table.ForeignKey(
                        name: "fk_assets_assets_site_id_parent_asset_id",
                        columns: x => new { x.site_id, x.parent_asset_id },
                        principalSchema: "assets",
                        principalTable: "assets",
                        principalColumns: new[] { "site_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_assets_locations_site_id_current_location_id",
                        columns: x => new { x.site_id, x.current_location_id },
                        principalSchema: "assets",
                        principalTable: "locations",
                        principalColumns: new[] { "site_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assets_normalized_tag",
                schema: "assets",
                table: "assets",
                column: "normalized_tag",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_assets_qr_locator",
                schema: "assets",
                table: "assets",
                column: "qr_locator",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_assets_site_id_current_location_id",
                schema: "assets",
                table: "assets",
                columns: new[] { "site_id", "current_location_id" });

            migrationBuilder.CreateIndex(
                name: "ix_assets_site_id_parent_asset_id",
                schema: "assets",
                table: "assets",
                columns: new[] { "site_id", "parent_asset_id" });

            migrationBuilder.CreateIndex(
                name: "ix_locations_site_id_code",
                schema: "assets",
                table: "locations",
                columns: new[] { "site_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_locations_site_id_parent_location_id",
                schema: "assets",
                table: "locations",
                columns: new[] { "site_id", "parent_location_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_locations_sites_site_id",
                schema: "assets",
                table: "locations",
                column: "site_id",
                principalSchema: "identity_access",
                principalTable: "sites",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_assets_sites_site_id",
                schema: "assets",
                table: "assets",
                column: "site_id",
                principalSchema: "identity_access",
                principalTable: "sites",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION assets.reject_site_id_change()
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

                CREATE TRIGGER locations_site_id_immutable
                    BEFORE UPDATE OF site_id ON assets.locations
                    FOR EACH ROW EXECUTE FUNCTION assets.reject_site_id_change();

                CREATE TRIGGER assets_site_id_immutable
                    BEFORE UPDATE OF site_id ON assets.assets
                    FOR EACH ROW EXECUTE FUNCTION assets.reject_site_id_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assets",
                schema: "assets");

            migrationBuilder.DropTable(
                name: "locations",
                schema: "assets");

            migrationBuilder.Sql("DROP FUNCTION assets.reject_site_id_change();");
        }
    }
}
