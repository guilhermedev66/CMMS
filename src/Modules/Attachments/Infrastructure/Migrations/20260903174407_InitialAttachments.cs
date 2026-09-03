using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cmms.Modules.Attachments.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "attachments");

            migrationBuilder.CreateTable(
                name: "attachments",
                schema: "attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    upload_intent_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_resource_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    parent_resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clean_storage_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    byte_size = table.Column<long>(type: "bigint", nullable: false),
                    pixel_width = table.Column<int>(type: "integer", nullable: false),
                    pixel_height = table.Column<int>(type: "integer", nullable: false),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    unlinked_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attachments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "upload_intents",
                schema: "attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_resource_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    parent_resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quarantine_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    declared_content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    max_bytes = table.Column<long>(type: "bigint", nullable: false),
                    original_file_name_for_display = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    uploaded_byte_count = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_upload_intents", x => x.id);
                    table.CheckConstraint("ck_upload_intents_status", "status IN ('Pending', 'Uploaded', 'Active', 'Expired', 'Rejected')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_attachments_clean_storage_key",
                schema: "attachments",
                table: "attachments",
                column: "clean_storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_attachments_parent_resource_type_parent_resource_id",
                schema: "attachments",
                table: "attachments",
                columns: new[] { "parent_resource_type", "parent_resource_id" });

            migrationBuilder.CreateIndex(
                name: "ix_attachments_upload_intent_id",
                schema: "attachments",
                table: "attachments",
                column: "upload_intent_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_upload_intents_parent_resource_type_parent_resource_id",
                schema: "attachments",
                table: "upload_intents",
                columns: new[] { "parent_resource_type", "parent_resource_id" });

            migrationBuilder.CreateIndex(
                name: "ix_upload_intents_quarantine_key",
                schema: "attachments",
                table: "upload_intents",
                column: "quarantine_key",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_attachments_sites_site_id",
                schema: "attachments",
                table: "attachments",
                column: "site_id",
                principalSchema: "identity_access",
                principalTable: "sites",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_upload_intents_sites_site_id",
                schema: "attachments",
                table: "upload_intents",
                column: "site_id",
                principalSchema: "identity_access",
                principalTable: "sites",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION attachments.reject_site_id_change()
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

                CREATE TRIGGER attachments_site_id_immutable
                    BEFORE UPDATE OF site_id ON attachments.attachments
                    FOR EACH ROW EXECUTE FUNCTION attachments.reject_site_id_change();

                CREATE TRIGGER upload_intents_site_id_immutable
                    BEFORE UPDATE OF site_id ON attachments.upload_intents
                    FOR EACH ROW EXECUTE FUNCTION attachments.reject_site_id_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attachments",
                schema: "attachments");

            migrationBuilder.DropTable(
                name: "upload_intents",
                schema: "attachments");

            migrationBuilder.Sql("DROP FUNCTION attachments.reject_site_id_change();");
        }
    }
}
