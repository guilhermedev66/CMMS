using Cmms.Modules.IdentityAccess.Domain;

namespace Cmms.Modules.IdentityAccess.Infrastructure;

internal static class RolePermissionSeed
{
    public static IReadOnlyList<RolePermission> All { get; } = Build();

    private static IReadOnlyList<RolePermission> Build()
    {
        var grants = new List<RolePermission>();

        foreach (var permission in PermissionCatalog.All)
        {
            var scope = permission is PermissionCatalog.UsersManage or PermissionCatalog.SitesManage
                ? PermissionScope.CompanyGlobal
                : permission.StartsWith("attachments.", StringComparison.Ordinal)
                    ? PermissionScope.ParentResource
                    : permission.EndsWith(".own", StringComparison.Ordinal)
                        ? PermissionScope.OwnRecord
                        : permission.EndsWith(".assigned", StringComparison.Ordinal)
                            ? PermissionScope.OwnAssignment
                            : PermissionScope.AllSites;

            grants.Add(new RolePermission(
                RoleCode.Admin,
                permission,
                scope,
                scope == PermissionScope.ParentResource ? "inherit_parent_authorization" : null));
        }

        AddSiteGrants(grants, RoleCode.Planner,
        [
            PermissionCatalog.AssetsCreate, PermissionCatalog.AssetsEdit,
            PermissionCatalog.AssetsCriticalityChange, PermissionCatalog.AssetsRead,
            PermissionCatalog.RequestsCreate, PermissionCatalog.RequestsReadOwn,
            PermissionCatalog.RequestsReadAll, PermissionCatalog.RequestsConvert,
            PermissionCatalog.RequestsReject, PermissionCatalog.RequestsCancelOwn,
            PermissionCatalog.WorkOrdersCreate, PermissionCatalog.WorkOrdersPlan,
            PermissionCatalog.WorkOrdersSchedule, PermissionCatalog.WorkOrdersPrioritize,
            PermissionCatalog.WorkOrdersSelfClaim, PermissionCatalog.WorkOrdersAssign,
            PermissionCatalog.WorkOrdersReassign, PermissionCatalog.WorkOrdersUnassign,
            PermissionCatalog.WorkOrdersReadAssigned, PermissionCatalog.WorkOrdersReadAll,
            PermissionCatalog.WorkOrdersExecute, PermissionCatalog.WorkOrdersComplete,
            PermissionCatalog.WorkOrdersClose, PermissionCatalog.WorkOrdersReopen,
            PermissionCatalog.WorkOrdersCancel, PermissionCatalog.PlansManage,
            PermissionCatalog.PlansRead, PermissionCatalog.CostsView,
            PermissionCatalog.AuditReadOwn, PermissionCatalog.AuditReadAll,
            PermissionCatalog.AuditExport
        ]);

        Add(grants, RoleCode.Technician, PermissionCatalog.AssetsRead, PermissionScope.MemberSite);
        Add(grants, RoleCode.Technician, PermissionCatalog.RequestsCreate, PermissionScope.MemberSite);
        Add(grants, RoleCode.Technician, PermissionCatalog.RequestsReadOwn, PermissionScope.OwnRecord, "created_by_self");
        Add(grants, RoleCode.Technician, PermissionCatalog.RequestsCancelOwn, PermissionScope.OwnRecord, "created_by_self_and_status_new");
        Add(grants, RoleCode.Technician, PermissionCatalog.WorkOrdersSelfClaim, PermissionScope.MemberSite, "unassigned_and_open");
        Add(grants, RoleCode.Technician, PermissionCatalog.WorkOrdersReadAssigned, PermissionScope.OwnAssignment, "assignee_id_is_self");
        Add(grants, RoleCode.Technician, PermissionCatalog.WorkOrdersExecute, PermissionScope.OwnAssignment, "assignee_id_is_self");
        Add(grants, RoleCode.Technician, PermissionCatalog.WorkOrdersComplete, PermissionScope.OwnAssignment, "assignee_id_is_self");
        Add(grants, RoleCode.Technician, PermissionCatalog.PlansRead, PermissionScope.MemberSite);
        Add(grants, RoleCode.Technician, PermissionCatalog.AuditReadOwn, PermissionScope.OwnRecord, "actor_or_assigned_work_is_self");

        Add(grants, RoleCode.Requester, PermissionCatalog.AssetsRead, PermissionScope.MemberSite, "limited_asset_fields");
        Add(grants, RoleCode.Requester, PermissionCatalog.RequestsCreate, PermissionScope.MemberSite);
        Add(grants, RoleCode.Requester, PermissionCatalog.RequestsReadOwn, PermissionScope.OwnRecord, "created_by_self");
        Add(grants, RoleCode.Requester, PermissionCatalog.RequestsCancelOwn, PermissionScope.OwnRecord, "created_by_self_and_status_new");
        Add(grants, RoleCode.Requester, PermissionCatalog.AuditReadOwn, PermissionScope.OwnRecord, "own_requests_only");

        foreach (var role in new[] { RoleCode.Planner, RoleCode.Technician, RoleCode.Requester })
        {
            Add(grants, role, PermissionCatalog.AttachmentsRead, PermissionScope.ParentResource, "inherit_parent_authorization");
            Add(grants, role, PermissionCatalog.AttachmentsWrite, PermissionScope.ParentResource, "inherit_parent_authorization");
            Add(grants, role, PermissionCatalog.AttachmentsUnlink, PermissionScope.ParentResource, "inherit_parent_authorization");
        }

        return grants;
    }

    private static void AddSiteGrants(List<RolePermission> grants, RoleCode role, IEnumerable<string> permissions)
    {
        foreach (var permission in permissions)
        {
            var scope = permission.EndsWith(".own", StringComparison.Ordinal)
                ? PermissionScope.OwnRecord
                : permission.EndsWith(".assigned", StringComparison.Ordinal)
                    ? PermissionScope.OwnAssignment
                    : PermissionScope.MemberSite;

            Add(grants, role, permission, scope);
        }
    }

    private static void Add(
        ICollection<RolePermission> grants,
        RoleCode role,
        string permission,
        PermissionScope scope,
        string? predicate = null) =>
        grants.Add(new RolePermission(role, permission, scope, predicate));
}
