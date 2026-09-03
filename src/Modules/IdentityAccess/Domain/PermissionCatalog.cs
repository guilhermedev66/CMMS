namespace Cmms.Modules.IdentityAccess.Domain;

public static class PermissionCatalog
{
    public const string UsersManage = "users.manage";
    public const string SitesManage = "sites.manage";
    public const string AssetsCreate = "assets.create";
    public const string AssetsEdit = "assets.edit";
    public const string AssetsCriticalityChange = "assets.criticality.change";
    public const string AssetsRead = "assets.read";
    public const string RequestsCreate = "requests.create";
    public const string RequestsReadOwn = "requests.read.own";
    public const string RequestsReadAll = "requests.read.all";
    public const string RequestsConvert = "requests.convert";
    public const string RequestsReject = "requests.reject";
    public const string RequestsCancelOwn = "requests.cancel.own";
    public const string WorkOrdersCreate = "workorders.create";
    public const string WorkOrdersPlan = "workorders.plan";
    public const string WorkOrdersSchedule = "workorders.schedule";
    public const string WorkOrdersPrioritize = "workorders.prioritize";
    public const string WorkOrdersSelfClaim = "workorders.selfclaim";
    public const string WorkOrdersAssign = "workorders.assign";
    public const string WorkOrdersReassign = "workorders.reassign";
    public const string WorkOrdersUnassign = "workorders.unassign";
    public const string WorkOrdersReadAssigned = "workorders.read.assigned";
    public const string WorkOrdersReadAll = "workorders.read.all";
    public const string WorkOrdersExecute = "workorders.execute";
    public const string WorkOrdersComplete = "workorders.complete";
    public const string WorkOrdersClose = "workorders.close";
    public const string WorkOrdersReopen = "workorders.reopen";
    public const string WorkOrdersCancel = "workorders.cancel";
    public const string PlansManage = "plans.manage";
    public const string PlansRead = "plans.read";
    public const string CostsView = "costs.view";
    public const string AuditReadOwn = "audit.read.own";
    public const string AuditReadAll = "audit.read.all";
    public const string AuditExport = "audit.export";
    public const string AttachmentsRead = "attachments.read";
    public const string AttachmentsWrite = "attachments.write";
    public const string AttachmentsUnlink = "attachments.unlink";

    public static IReadOnlyList<string> All { get; } =
    [
        UsersManage, SitesManage,
        AssetsCreate, AssetsEdit, AssetsCriticalityChange, AssetsRead,
        RequestsCreate, RequestsReadOwn, RequestsReadAll, RequestsConvert, RequestsReject, RequestsCancelOwn,
        WorkOrdersCreate, WorkOrdersPlan, WorkOrdersSchedule, WorkOrdersPrioritize,
        WorkOrdersSelfClaim, WorkOrdersAssign, WorkOrdersReassign, WorkOrdersUnassign,
        WorkOrdersReadAssigned, WorkOrdersReadAll, WorkOrdersExecute, WorkOrdersComplete,
        WorkOrdersClose, WorkOrdersReopen, WorkOrdersCancel,
        PlansManage, PlansRead, CostsView,
        AuditReadOwn, AuditReadAll, AuditExport,
        AttachmentsRead, AttachmentsWrite, AttachmentsUnlink
    ];
}
