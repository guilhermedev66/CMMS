using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Cmms.Modules.IdentityAccess.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialIdentityAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identity_access");

            migrationBuilder.CreateTable(
                name: "permission_definitions",
                schema: "identity_access",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permission_definitions", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "role_definitions",
                schema: "identity_access",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    scope = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_definitions", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "sites",
                schema: "identity_access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    time_zone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sites", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "identity_access",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                schema: "identity_access",
                columns: table => new
                {
                    role_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    permission_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    scope = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    resource_predicate = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_permissions", x => new { x.role_code, x.permission_code });
                    table.ForeignKey(
                        name: "fk_role_permissions_permission_definitions_permission_code",
                        column: x => x.permission_code,
                        principalSchema: "identity_access",
                        principalTable: "permission_definitions",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_role_permissions_role_definitions_role_code",
                        column: x => x.role_code,
                        principalSchema: "identity_access",
                        principalTable: "role_definitions",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "company_role_assignments",
                schema: "identity_access",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    assigned_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_role_assignments", x => new { x.user_id, x.role_code });
                    table.CheckConstraint("ck_company_role_assignments_admin_only", "role_code = 'Admin'");
                    table.ForeignKey(
                        name: "fk_company_role_assignments_role_definitions_role_code",
                        column: x => x.role_code,
                        principalSchema: "identity_access",
                        principalTable: "role_definitions",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_company_role_assignments_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity_access",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "site_memberships",
                schema: "identity_access",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    assigned_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_site_memberships", x => new { x.user_id, x.site_id });
                    table.CheckConstraint("ck_site_memberships_site_roles_only", "role_code IN ('Planner', 'Technician', 'Requester')");
                    table.ForeignKey(
                        name: "fk_site_memberships_role_definitions_role_code",
                        column: x => x.role_code,
                        principalSchema: "identity_access",
                        principalTable: "role_definitions",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_site_memberships_sites_site_id",
                        column: x => x.site_id,
                        principalSchema: "identity_access",
                        principalTable: "sites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_site_memberships_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity_access",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_claims",
                schema: "identity_access",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_claims_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity_access",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_logins",
                schema: "identity_access",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_user_logins_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity_access",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                schema: "identity_access",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_user_tokens_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity_access",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "identity_access",
                table: "permission_definitions",
                column: "code",
                values: new object[]
                {
                    "assets.create",
                    "assets.criticality.change",
                    "assets.edit",
                    "assets.read",
                    "attachments.read",
                    "attachments.unlink",
                    "attachments.write",
                    "audit.export",
                    "audit.read.all",
                    "audit.read.own",
                    "costs.view",
                    "plans.manage",
                    "plans.read",
                    "requests.cancel.own",
                    "requests.convert",
                    "requests.create",
                    "requests.read.all",
                    "requests.read.own",
                    "requests.reject",
                    "sites.manage",
                    "users.manage",
                    "workorders.assign",
                    "workorders.cancel",
                    "workorders.close",
                    "workorders.complete",
                    "workorders.create",
                    "workorders.execute",
                    "workorders.plan",
                    "workorders.prioritize",
                    "workorders.read.all",
                    "workorders.read.assigned",
                    "workorders.reassign",
                    "workorders.reopen",
                    "workorders.schedule",
                    "workorders.selfclaim",
                    "workorders.unassign"
                });

            migrationBuilder.InsertData(
                schema: "identity_access",
                table: "role_definitions",
                columns: new[] { "code", "scope" },
                values: new object[,]
                {
                    { "Admin", "Company" },
                    { "Planner", "Site" },
                    { "Requester", "Site" },
                    { "Technician", "Site" }
                });

            migrationBuilder.InsertData(
                schema: "identity_access",
                table: "role_permissions",
                columns: new[] { "permission_code", "role_code", "resource_predicate", "scope" },
                values: new object[,]
                {
                    { "assets.create", "Admin", null, "AllSites" },
                    { "assets.criticality.change", "Admin", null, "AllSites" },
                    { "assets.edit", "Admin", null, "AllSites" },
                    { "assets.read", "Admin", null, "AllSites" },
                    { "attachments.read", "Admin", "inherit_parent_authorization", "ParentResource" },
                    { "attachments.unlink", "Admin", "inherit_parent_authorization", "ParentResource" },
                    { "attachments.write", "Admin", "inherit_parent_authorization", "ParentResource" },
                    { "audit.export", "Admin", null, "AllSites" },
                    { "audit.read.all", "Admin", null, "AllSites" },
                    { "audit.read.own", "Admin", null, "OwnRecord" },
                    { "costs.view", "Admin", null, "AllSites" },
                    { "plans.manage", "Admin", null, "AllSites" },
                    { "plans.read", "Admin", null, "AllSites" },
                    { "requests.cancel.own", "Admin", null, "OwnRecord" },
                    { "requests.convert", "Admin", null, "AllSites" },
                    { "requests.create", "Admin", null, "AllSites" },
                    { "requests.read.all", "Admin", null, "AllSites" },
                    { "requests.read.own", "Admin", null, "OwnRecord" },
                    { "requests.reject", "Admin", null, "AllSites" },
                    { "sites.manage", "Admin", null, "CompanyGlobal" },
                    { "users.manage", "Admin", null, "CompanyGlobal" },
                    { "workorders.assign", "Admin", null, "AllSites" },
                    { "workorders.cancel", "Admin", null, "AllSites" },
                    { "workorders.close", "Admin", null, "AllSites" },
                    { "workorders.complete", "Admin", null, "AllSites" },
                    { "workorders.create", "Admin", null, "AllSites" },
                    { "workorders.execute", "Admin", null, "AllSites" },
                    { "workorders.plan", "Admin", null, "AllSites" },
                    { "workorders.prioritize", "Admin", null, "AllSites" },
                    { "workorders.read.all", "Admin", null, "AllSites" },
                    { "workorders.read.assigned", "Admin", null, "OwnAssignment" },
                    { "workorders.reassign", "Admin", null, "AllSites" },
                    { "workorders.reopen", "Admin", null, "AllSites" },
                    { "workorders.schedule", "Admin", null, "AllSites" },
                    { "workorders.selfclaim", "Admin", null, "AllSites" },
                    { "workorders.unassign", "Admin", null, "AllSites" },
                    { "assets.create", "Planner", null, "MemberSite" },
                    { "assets.criticality.change", "Planner", null, "MemberSite" },
                    { "assets.edit", "Planner", null, "MemberSite" },
                    { "assets.read", "Planner", null, "MemberSite" },
                    { "attachments.read", "Planner", "inherit_parent_authorization", "ParentResource" },
                    { "attachments.unlink", "Planner", "inherit_parent_authorization", "ParentResource" },
                    { "attachments.write", "Planner", "inherit_parent_authorization", "ParentResource" },
                    { "audit.export", "Planner", null, "MemberSite" },
                    { "audit.read.all", "Planner", null, "MemberSite" },
                    { "audit.read.own", "Planner", null, "OwnRecord" },
                    { "costs.view", "Planner", null, "MemberSite" },
                    { "plans.manage", "Planner", null, "MemberSite" },
                    { "plans.read", "Planner", null, "MemberSite" },
                    { "requests.cancel.own", "Planner", null, "OwnRecord" },
                    { "requests.convert", "Planner", null, "MemberSite" },
                    { "requests.create", "Planner", null, "MemberSite" },
                    { "requests.read.all", "Planner", null, "MemberSite" },
                    { "requests.read.own", "Planner", null, "OwnRecord" },
                    { "requests.reject", "Planner", null, "MemberSite" },
                    { "workorders.assign", "Planner", null, "MemberSite" },
                    { "workorders.cancel", "Planner", null, "MemberSite" },
                    { "workorders.close", "Planner", null, "MemberSite" },
                    { "workorders.complete", "Planner", null, "MemberSite" },
                    { "workorders.create", "Planner", null, "MemberSite" },
                    { "workorders.execute", "Planner", null, "MemberSite" },
                    { "workorders.plan", "Planner", null, "MemberSite" },
                    { "workorders.prioritize", "Planner", null, "MemberSite" },
                    { "workorders.read.all", "Planner", null, "MemberSite" },
                    { "workorders.read.assigned", "Planner", null, "OwnAssignment" },
                    { "workorders.reassign", "Planner", null, "MemberSite" },
                    { "workorders.reopen", "Planner", null, "MemberSite" },
                    { "workorders.schedule", "Planner", null, "MemberSite" },
                    { "workorders.selfclaim", "Planner", null, "MemberSite" },
                    { "workorders.unassign", "Planner", null, "MemberSite" },
                    { "assets.read", "Requester", "limited_asset_fields", "MemberSite" },
                    { "attachments.read", "Requester", "inherit_parent_authorization", "ParentResource" },
                    { "attachments.unlink", "Requester", "inherit_parent_authorization", "ParentResource" },
                    { "attachments.write", "Requester", "inherit_parent_authorization", "ParentResource" },
                    { "audit.read.own", "Requester", "own_requests_only", "OwnRecord" },
                    { "requests.cancel.own", "Requester", "created_by_self_and_status_new", "OwnRecord" },
                    { "requests.create", "Requester", null, "MemberSite" },
                    { "requests.read.own", "Requester", "created_by_self", "OwnRecord" },
                    { "assets.read", "Technician", null, "MemberSite" },
                    { "attachments.read", "Technician", "inherit_parent_authorization", "ParentResource" },
                    { "attachments.unlink", "Technician", "inherit_parent_authorization", "ParentResource" },
                    { "attachments.write", "Technician", "inherit_parent_authorization", "ParentResource" },
                    { "audit.read.own", "Technician", "actor_or_assigned_work_is_self", "OwnRecord" },
                    { "plans.read", "Technician", null, "MemberSite" },
                    { "requests.cancel.own", "Technician", "created_by_self_and_status_new", "OwnRecord" },
                    { "requests.create", "Technician", null, "MemberSite" },
                    { "requests.read.own", "Technician", "created_by_self", "OwnRecord" },
                    { "workorders.complete", "Technician", "assignee_id_is_self", "OwnAssignment" },
                    { "workorders.execute", "Technician", "assignee_id_is_self", "OwnAssignment" },
                    { "workorders.read.assigned", "Technician", "assignee_id_is_self", "OwnAssignment" },
                    { "workorders.selfclaim", "Technician", "unassigned_and_open", "MemberSite" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_company_role_assignments_role_code",
                schema: "identity_access",
                table: "company_role_assignments",
                column: "role_code");

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_permission_code",
                schema: "identity_access",
                table: "role_permissions",
                column: "permission_code");

            migrationBuilder.CreateIndex(
                name: "ix_site_memberships_role_code",
                schema: "identity_access",
                table: "site_memberships",
                column: "role_code");

            migrationBuilder.CreateIndex(
                name: "ix_site_memberships_site_id_role_code_is_active",
                schema: "identity_access",
                table: "site_memberships",
                columns: new[] { "site_id", "role_code", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_sites_code",
                schema: "identity_access",
                table: "sites",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_claims_user_id",
                schema: "identity_access",
                table: "user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_logins_user_id",
                schema: "identity_access",
                table: "user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "identity_access",
                table: "users",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "identity_access",
                table: "users",
                column: "normalized_user_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_role_assignments",
                schema: "identity_access");

            migrationBuilder.DropTable(
                name: "role_permissions",
                schema: "identity_access");

            migrationBuilder.DropTable(
                name: "site_memberships",
                schema: "identity_access");

            migrationBuilder.DropTable(
                name: "user_claims",
                schema: "identity_access");

            migrationBuilder.DropTable(
                name: "user_logins",
                schema: "identity_access");

            migrationBuilder.DropTable(
                name: "user_tokens",
                schema: "identity_access");

            migrationBuilder.DropTable(
                name: "permission_definitions",
                schema: "identity_access");

            migrationBuilder.DropTable(
                name: "role_definitions",
                schema: "identity_access");

            migrationBuilder.DropTable(
                name: "sites",
                schema: "identity_access");

            migrationBuilder.DropTable(
                name: "users",
                schema: "identity_access");
        }
    }
}
