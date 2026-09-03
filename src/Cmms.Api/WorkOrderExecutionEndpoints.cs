using System.Security.Claims;
using System.Text.Json;
using Cmms.BuildingBlocks.Database;
using Cmms.Modules.Attachments.Infrastructure;
using Cmms.Modules.Audit.Application;
using Cmms.Modules.Audit.Infrastructure;
using Cmms.Modules.IdentityAccess.Application;
using Cmms.Modules.IdentityAccess.Domain;
using Cmms.Modules.WorkManagement.Domain;
using Cmms.Modules.WorkManagement.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Cmms.Api;

/// <summary>
/// Checklist items, downtime intervals, and part usages — the M4 "execution" child data hanging
/// off a Work Order, per docs/01-domain-and-workflows.md §§ "Checklist item types", "Downtime
/// tracking", "Parts &amp; costs (lean scope)". Every mutating endpoint here follows the same
/// root-lock protocol as <see cref="WorkOrdersEndpoints"/>'s <c>TransitionAsync</c> (docs/02:
/// "Child edit ... races with completion/closure ... Both commands lock the Work Order root") —
/// there is no separate, unlocked write path for child data.
///
/// Checklist item *definition* (create) is Planner/Admin only (<see
/// cref="PermissionCatalog.WorkOrdersPlan"/>) — docs/01: the definition is authored, then the
/// assignee only ever *resolves* it (<see cref="PermissionCatalog.WorkOrdersExecute"/>, same
/// assignee-or-planner actor gate as Start Work/Mark Completed). Downtime and parts are both
/// assignee-or-planner writes under <see cref="PermissionCatalog.WorkOrdersExecute"/> — they are
/// facts the person actually doing the work records, not planning artifacts.
/// </summary>
internal static class WorkOrderExecutionEndpoints
{
    public static IEndpointRouteBuilder MapWorkOrderExecutionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var workOrders = endpoints.MapGroup("/work-orders").WithTags("WorkOrderExecution").RequireAuthorization();

        workOrders.MapGet("/{id:guid}/checklist-items", ListChecklistItemsAsync);
        workOrders.MapPost("/{id:guid}/checklist-items", CreateChecklistItemAsync);
        workOrders.MapPost("/{id:guid}/checklist-items/{itemId:guid}/resolve", ResolveChecklistItemAsync);

        workOrders.MapGet("/{id:guid}/downtime-intervals", ListDowntimeIntervalsAsync);
        workOrders.MapPost("/{id:guid}/downtime-intervals", OpenDowntimeIntervalAsync);
        workOrders.MapPost("/{id:guid}/downtime-intervals/{intervalId:guid}/close", CloseDowntimeIntervalAsync);

        workOrders.MapGet("/{id:guid}/part-usages", ListPartUsagesAsync);
        workOrders.MapPost("/{id:guid}/part-usages", PostPartUsageAsync);

        return endpoints;
    }

    // ---------- Shared read-authorization (mirrors WorkOrdersEndpoints.GetWorkOrderAsync) ----------

    private static async Task<(WorkOrder? WorkOrder, bool Authorized)> LoadForReadAsync(
        Guid id,
        ClaimsPrincipal user,
        WorkManagementDbContext workOrdersDb,
        IPermissionEvaluator permissions,
        CancellationToken cancellationToken)
    {
        var workOrder = await workOrdersDb.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (workOrder is null)
        {
            return (null, false);
        }

        var userId = permissions.GetUserId(user);
        var canReadAll = await permissions.HasPermissionAsync(user, PermissionCatalog.WorkOrdersReadAll, workOrder.SiteId, cancellationToken);
        var canReadAssigned = userId == workOrder.AssigneeId &&
            await permissions.HasPermissionAsync(user, PermissionCatalog.WorkOrdersReadAssigned, workOrder.SiteId, cancellationToken);

        return (workOrder, canReadAll || canReadAssigned);
    }

    private static bool RequireAssigneeOrPlanner(WorkOrder workOrder, RoleCode? role, Guid actorUserId) =>
        role is RoleCode.Admin or RoleCode.Planner || workOrder.AssigneeId == actorUserId;

    // ---------- Checklist items ----------

    private static async Task<IResult> ListChecklistItemsAsync(
        Guid id,
        int? executionCycle,
        ClaimsPrincipal user,
        WorkManagementDbContext workOrdersDb,
        IPermissionEvaluator permissions,
        CancellationToken cancellationToken)
    {
        var (workOrder, authorized) = await LoadForReadAsync(id, user, workOrdersDb, permissions, cancellationToken);
        if (workOrder is null || !authorized)
        {
            return Results.NotFound();
        }

        var cycle = executionCycle ?? workOrder.ExecutionCycle;
        var items = await workOrdersDb.ChecklistItems.AsNoTracking()
            .Where(item => item.WorkOrderId == id && item.ExecutionCycle == cycle)
            .OrderBy(item => item.SortOrder)
            .ToListAsync(cancellationToken);

        return Results.Ok(items.Select(ChecklistItemResponse.From));
    }

    private static async Task<IResult> CreateChecklistItemAsync(
        Guid id,
        CreateChecklistItemRequest request,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        IConfiguration configuration,
        IPermissionEvaluator permissions,
        IAuditEventWriter auditWriter,
        CancellationToken cancellationToken)
    {
        if (!await AntiforgeryHelpers.HasValidAntiforgeryTokenAsync(httpContext, antiforgery))
        {
            return Results.BadRequest(new { error = "Invalid anti-forgery token." });
        }

        if (string.IsNullOrWhiteSpace(request.Label))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["label"] = ["Label is required."] });
        }

        return await WithRootLockedWorkOrderAsync(
            id,
            httpContext,
            configuration,
            permissions,
            auditWriter,
            PermissionCatalog.WorkOrdersPlan,
            actorCheck: null,
            requireNotTerminal: true,
            action: "checklistitem.created",
            mutate: async (workOrdersDb, workOrder, actorUserId, cancellationToken) =>
            {
                if (request.SafetyCritical && request.ItemType != ChecklistItemType.Boolean)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["safetyCritical"] = ["safety_critical only applies to Boolean items."]
                    });
                }

                var nextSortOrder = 1 + await workOrdersDb.ChecklistItems
                    .Where(i => i.WorkOrderId == id && i.ExecutionCycle == workOrder.ExecutionCycle)
                    .Select(i => (int?)i.SortOrder)
                    .MaxAsync(cancellationToken) ?? 0;

                var item = new ChecklistItem(
                    id,
                    workOrder.SiteId,
                    workOrder.ExecutionCycle,
                    nextSortOrder,
                    request.ItemType,
                    request.Label,
                    request.IsRequired,
                    request.SafetyCritical,
                    request.NumericMinValue,
                    request.NumericMaxValue,
                    request.NumericUnit,
                    request.SingleSelectOptionsCsv);

                workOrdersDb.ChecklistItems.Add(item);
                return Results.Created($"/work-orders/{id}/checklist-items/{item.Id}", ChecklistItemResponse.From(item));
            },
            cancellationToken: cancellationToken);
    }

    private static async Task<IResult> ResolveChecklistItemAsync(
        Guid id,
        Guid itemId,
        ResolveChecklistItemRequest request,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        IConfiguration configuration,
        IPermissionEvaluator permissions,
        IAuditEventWriter auditWriter,
        CancellationToken cancellationToken)
    {
        if (!await AntiforgeryHelpers.HasValidAntiforgeryTokenAsync(httpContext, antiforgery))
        {
            return Results.BadRequest(new { error = "Invalid anti-forgery token." });
        }

        var connectionString = configuration.GetConnectionString("Cmms")
            ?? throw new InvalidOperationException("Connection string 'Cmms' is not configured.");

        await using var transactionScope = await SharedTransactionScope.BeginAsync(connectionString, cancellationToken);
        await using var workOrdersDb = transactionScope.CreateContext<WorkManagementDbContext>(options => new WorkManagementDbContext(options));
        await using var auditDb = transactionScope.CreateContext<AuditDbContext>(options => new AuditDbContext(options));

        var workOrder = await workOrdersDb.WorkOrders
            .FromSqlInterpolated($"SELECT * FROM work_management.work_orders WHERE id = {id} FOR UPDATE")
            .FirstOrDefaultAsync(cancellationToken);
        if (workOrder is null)
        {
            return Results.NotFound();
        }

        var actorUserId = permissions.GetUserId(httpContext.User);
        if (actorUserId is null || !await permissions.HasPermissionAsync(httpContext.User, PermissionCatalog.WorkOrdersExecute, workOrder.SiteId, cancellationToken))
        {
            return Results.NotFound();
        }

        var role = await permissions.GetEffectiveRoleAsync(httpContext.User, workOrder.SiteId, cancellationToken);
        if (!RequireAssigneeOrPlanner(workOrder, role, actorUserId.Value))
        {
            return Results.NotFound();
        }

        if (workOrder.Status != WorkOrderStatus.InProgress)
        {
            return Results.Conflict(new { error = "Checklist items can only be resolved while the Work Order is InProgress." });
        }

        var item = await workOrdersDb.ChecklistItems.FirstOrDefaultAsync(
            i => i.Id == itemId && i.WorkOrderId == id && i.ExecutionCycle == workOrder.ExecutionCycle,
            cancellationToken: cancellationToken);
        if (item is null)
        {
            return Results.NotFound();
        }

        if (item.ItemType == ChecklistItemType.PhotoRequired && request.AttachmentId is not null)
        {
            // Re-verify the referenced attachment is a live, still-linked, still-this-site,
            // still-this-Work-Order object — never trust the client's claim that a given
            // attachment id is "Active" (docs/02's PhotoRequired race guard).
            await using var attachmentsDb = transactionScope.CreateContext<AttachmentsDbContext>(options => new AttachmentsDbContext(options));
            var attachmentValid = await attachmentsDb.Attachments.AsNoTracking().AnyAsync(
                a => a.Id == request.AttachmentId &&
                     a.SiteId == workOrder.SiteId &&
                     a.ParentResourceId == id &&
                     a.UnlinkedAtUtc == null,
                cancellationToken);
            if (!attachmentValid)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["attachmentId"] = ["This attachment is not an active, linked evidence photo for this Work Order."]
                });
            }
        }

        try
        {
            item.Resolve(actorUserId.Value, request.BooleanValue, request.NumericValue, request.SelectedOption, request.NoteText, request.AttachmentId);
        }
        catch (InvalidChecklistItemOperationException ex)
        {
            await transactionScope.RollbackAsync(cancellationToken);
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["value"] = [ex.Message] });
        }

        await workOrdersDb.SaveChangesAsync(cancellationToken);

        await auditWriter.WriteAsync(
            auditDb,
            new AuditEventEntry(
                ActorUserId: actorUserId,
                Action: "checklistitem.resolved",
                ResourceType: "ChecklistItem",
                ResourceId: item.Id,
                SiteId: workOrder.SiteId,
                CorrelationId: null,
                Reason: null,
                BeforeJson: JsonSerializer.Serialize(new { isResolved = false }),
                AfterJson: JsonSerializer.Serialize(new { isResolved = true, item.NumericOutOfTolerance })),
            cancellationToken: cancellationToken);

        await transactionScope.CommitAsync(cancellationToken);

        return Results.Ok(ChecklistItemResponse.From(item));
    }

    // ---------- Downtime intervals ----------

    private static async Task<IResult> ListDowntimeIntervalsAsync(
        Guid id,
        int? executionCycle,
        ClaimsPrincipal user,
        WorkManagementDbContext workOrdersDb,
        IPermissionEvaluator permissions,
        CancellationToken cancellationToken)
    {
        var (workOrder, authorized) = await LoadForReadAsync(id, user, workOrdersDb, permissions, cancellationToken);
        if (workOrder is null || !authorized)
        {
            return Results.NotFound();
        }

        var cycle = executionCycle ?? workOrder.ExecutionCycle;
        var intervals = await workOrdersDb.DowntimeIntervals.AsNoTracking()
            .Where(interval => interval.WorkOrderId == id && interval.ExecutionCycle == cycle)
            .OrderBy(interval => interval.StartedAtUtc)
            .ToListAsync(cancellationToken);

        return Results.Ok(intervals.Select(DowntimeIntervalResponse.From));
    }

    private static async Task<IResult> OpenDowntimeIntervalAsync(
        Guid id,
        OpenDowntimeIntervalRequest request,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        IConfiguration configuration,
        IPermissionEvaluator permissions,
        IAuditEventWriter auditWriter,
        CancellationToken cancellationToken)
    {
        if (!await AntiforgeryHelpers.HasValidAntiforgeryTokenAsync(httpContext, antiforgery))
        {
            return Results.BadRequest(new { error = "Invalid anti-forgery token." });
        }

        return await WithRootLockedWorkOrderAsync(
            id,
            httpContext,
            configuration,
            permissions,
            auditWriter,
            PermissionCatalog.WorkOrdersExecute,
            actorCheck: RequireAssigneeOrPlanner,
            requireNotTerminal: false,
            requireInProgress: true,
            action: "downtimeinterval.opened",
            mutate: (workOrdersDb, workOrder, actorUserId, cancellationToken) =>
            {
                if (workOrder.AssetId is null)
                {
                    return Task.FromResult(Results.ValidationProblem(new Dictionary<string, string[]>
                    {
                        ["assetId"] = ["Downtime cannot be recorded on a Work Order with no linked asset."]
                    }));
                }

                var interval = new DowntimeInterval(id, workOrder.SiteId, workOrder.AssetId.Value, workOrder.ExecutionCycle, request.Classification, actorUserId);
                workOrdersDb.DowntimeIntervals.Add(interval);
                return Task.FromResult(Results.Created($"/work-orders/{id}/downtime-intervals/{interval.Id}", DowntimeIntervalResponse.From(interval)));
            },
            cancellationToken: cancellationToken,
            mapUniqueViolationTo: () => Results.Conflict(new
            {
                error = "This asset already has an open full-stop downtime interval that overlaps this one."
            }));
    }

    private static async Task<IResult> CloseDowntimeIntervalAsync(
        Guid id,
        Guid intervalId,
        CloseDowntimeIntervalRequest request,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        IConfiguration configuration,
        IPermissionEvaluator permissions,
        IAuditEventWriter auditWriter,
        CancellationToken cancellationToken)
    {
        if (!await AntiforgeryHelpers.HasValidAntiforgeryTokenAsync(httpContext, antiforgery))
        {
            return Results.BadRequest(new { error = "Invalid anti-forgery token." });
        }

        if (string.IsNullOrWhiteSpace(request.CauseMechanism))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["causeMechanism"] = ["A cause mechanism is required to close a downtime interval."] });
        }

        return await WithRootLockedWorkOrderAsync(
            id,
            httpContext,
            configuration,
            permissions,
            auditWriter,
            PermissionCatalog.WorkOrdersExecute,
            actorCheck: RequireAssigneeOrPlanner,
            requireNotTerminal: true,
            action: "downtimeinterval.closed",
            mutate: async (workOrdersDb, workOrder, actorUserId, cancellationToken) =>
            {
                var interval = await workOrdersDb.DowntimeIntervals.FirstOrDefaultAsync(
                    i => i.Id == intervalId && i.WorkOrderId == id && i.ExecutionCycle == workOrder.ExecutionCycle,
                    cancellationToken);
                if (interval is null)
                {
                    return Results.NotFound();
                }

                try
                {
                    interval.Close(request.CauseCategory, request.CauseMechanism);
                }
                catch (InvalidDowntimeIntervalOperationException ex)
                {
                    return Results.Conflict(new { error = ex.Message });
                }

                return Results.Ok(DowntimeIntervalResponse.From(interval));
            },
            cancellationToken: cancellationToken);
    }

    // ---------- Part usages ----------

    private static async Task<IResult> ListPartUsagesAsync(
        Guid id,
        int? executionCycle,
        ClaimsPrincipal user,
        WorkManagementDbContext workOrdersDb,
        IPermissionEvaluator permissions,
        CancellationToken cancellationToken)
    {
        var (workOrder, authorized) = await LoadForReadAsync(id, user, workOrdersDb, permissions, cancellationToken);
        if (workOrder is null || !authorized)
        {
            return Results.NotFound();
        }

        var canViewCosts = await permissions.HasPermissionAsync(user, PermissionCatalog.CostsView, workOrder.SiteId, cancellationToken);

        var cycle = executionCycle ?? workOrder.ExecutionCycle;
        var usages = await workOrdersDb.PartUsages.AsNoTracking()
            .Where(usage => usage.WorkOrderId == id && usage.ExecutionCycle == cycle)
            .OrderByDescending(usage => usage.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Results.Ok(usages.Select(usage => PartUsageResponse.From(usage, canViewCosts)));
    }

    private static async Task<IResult> PostPartUsageAsync(
        Guid id,
        PostPartUsageRequest request,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        IConfiguration configuration,
        IPermissionEvaluator permissions,
        IAuditEventWriter auditWriter,
        CancellationToken cancellationToken)
    {
        if (!await AntiforgeryHelpers.HasValidAntiforgeryTokenAsync(httpContext, antiforgery))
        {
            return Results.BadRequest(new { error = "Invalid anti-forgery token." });
        }

        if (string.IsNullOrWhiteSpace(request.PartName) || request.Quantity <= 0 || request.UnitCost < 0 || string.IsNullOrWhiteSpace(request.Currency))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["value"] = ["A valid part name, positive quantity, non-negative unit cost, and currency are required."] });
        }

        return await WithRootLockedWorkOrderAsync(
            id,
            httpContext,
            configuration,
            permissions,
            auditWriter,
            PermissionCatalog.WorkOrdersExecute,
            actorCheck: RequireAssigneeOrPlanner,
            requireNotTerminal: true,
            action: "partusage.posted",
            mutate: async (workOrdersDb, workOrder, actorUserId, cancellationToken) =>
            {
                // Idempotency replay, per docs/02: "a client-supplied idempotency key deduplicates
                // a retried insert" — re-checked (and re-authorized, since we're already inside the
                // same authorized/root-locked call) on every replay, not just the first.
                if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
                {
                    var existing = await workOrdersDb.PartUsages.AsNoTracking().FirstOrDefaultAsync(
                        u => u.WorkOrderId == id && u.IdempotencyKey == request.IdempotencyKey, cancellationToken);
                    if (existing is not null)
                    {
                        var canViewCostsReplay = await permissions.HasPermissionAsync(httpContext.User, PermissionCatalog.CostsView, workOrder.SiteId, cancellationToken);
                        return Results.Ok(PartUsageResponse.From(existing, canViewCostsReplay));
                    }
                }

                var usage = new PartUsage(id, workOrder.SiteId, workOrder.ExecutionCycle, request.PartName, request.PartCode, request.Quantity, request.UnitCost, request.Currency, actorUserId, request.IdempotencyKey);
                workOrdersDb.PartUsages.Add(usage);
                var canViewCosts = await permissions.HasPermissionAsync(httpContext.User, PermissionCatalog.CostsView, workOrder.SiteId, cancellationToken);
                return Results.Created($"/work-orders/{id}/part-usages/{usage.Id}", PartUsageResponse.From(usage, canViewCosts));
            },
            cancellationToken: cancellationToken,
            mapUniqueViolationTo: () => Results.Conflict(new { error = "This idempotency key was already used for a different part-usage posting." }));
    }

    // ---------- Shared root-locked child-mutation machinery ----------

    private delegate bool ActorCheck(WorkOrder workOrder, RoleCode? role, Guid actorUserId);

    private static async Task<IResult> WithRootLockedWorkOrderAsync(
        Guid id,
        HttpContext httpContext,
        IConfiguration configuration,
        IPermissionEvaluator permissions,
        IAuditEventWriter auditWriter,
        string permissionCode,
        ActorCheck? actorCheck,
        bool requireNotTerminal,
        string action,
        Func<WorkManagementDbContext, WorkOrder, Guid, CancellationToken, Task<IResult>> mutate,
        CancellationToken cancellationToken,
        bool requireInProgress = false,
        Func<IResult>? mapUniqueViolationTo = null)
    {
        var connectionString = configuration.GetConnectionString("Cmms")
            ?? throw new InvalidOperationException("Connection string 'Cmms' is not configured.");

        await using var transactionScope = await SharedTransactionScope.BeginAsync(connectionString, cancellationToken);
        await using var workOrdersDb = transactionScope.CreateContext<WorkManagementDbContext>(options => new WorkManagementDbContext(options));
        await using var auditDb = transactionScope.CreateContext<AuditDbContext>(options => new AuditDbContext(options));

        var workOrder = await workOrdersDb.WorkOrders
            .FromSqlInterpolated($"SELECT * FROM work_management.work_orders WHERE id = {id} FOR UPDATE")
            .FirstOrDefaultAsync(cancellationToken);
        if (workOrder is null)
        {
            return Results.NotFound();
        }

        var actorUserId = permissions.GetUserId(httpContext.User);
        if (actorUserId is null || !await permissions.HasPermissionAsync(httpContext.User, permissionCode, workOrder.SiteId, cancellationToken))
        {
            return Results.NotFound();
        }

        if (actorCheck is not null)
        {
            var role = await permissions.GetEffectiveRoleAsync(httpContext.User, workOrder.SiteId, cancellationToken);
            if (!actorCheck(workOrder, role, actorUserId.Value))
            {
                return Results.NotFound();
            }
        }

        if (requireNotTerminal && workOrder.Status is WorkOrderStatus.Closed or WorkOrderStatus.Cancelled)
        {
            return Results.Conflict(new { error = $"This Work Order is {workOrder.Status} and no longer editable." });
        }

        if (requireInProgress && workOrder.Status != WorkOrderStatus.InProgress)
        {
            return Results.Conflict(new { error = "This operation requires the Work Order to be InProgress." });
        }

        // The mutate delegate only calls DbSet.Add/etc. (tracked, not yet sent to Postgres) — the
        // actual INSERT, and therefore any unique/exclusion constraint violation (e.g. the
        // FullStop-overlap exclusion constraint), only happens at SaveChangesAsync. Both calls need
        // to be inside the same try/catch for mapUniqueViolationTo to ever actually catch it.
        IResult result;
        try
        {
            result = await mutate(workOrdersDb, workOrder, actorUserId.Value, cancellationToken);

            // Every mutate delegate returns either a 2xx success result (Ok/Created) whose changes
            // should persist, or a problem result (NotFound/Conflict/ValidationProblem) signalling
            // the caller decided not to mutate — IStatusCodeHttpResult is the standard minimal-API
            // surface for telling those apart without pattern-matching every concrete result type.
            if (result is not IStatusCodeHttpResult { StatusCode: >= 200 and < 300 })
            {
                await transactionScope.RollbackAsync(cancellationToken);
                return result;
            }

            await workOrdersDb.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (mapUniqueViolationTo is not null && IsUniqueViolation(ex))
        {
            await transactionScope.RollbackAsync(cancellationToken);
            return mapUniqueViolationTo();
        }

        await auditWriter.WriteAsync(
            auditDb,
            new AuditEventEntry(
                ActorUserId: actorUserId,
                Action: action,
                ResourceType: "WorkOrder",
                ResourceId: workOrder.Id,
                SiteId: workOrder.SiteId,
                CorrelationId: null,
                Reason: null,
                BeforeJson: null,
                AfterJson: null),
            cancellationToken);

        await transactionScope.CommitAsync(cancellationToken);

        return result;
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: "23505" or "23P01" };
}

// ---------- Requests ----------

internal sealed record CreateChecklistItemRequest(
    ChecklistItemType ItemType,
    string Label,
    bool IsRequired,
    bool SafetyCritical = false,
    decimal? NumericMinValue = null,
    decimal? NumericMaxValue = null,
    string? NumericUnit = null,
    string? SingleSelectOptionsCsv = null);

internal sealed record ResolveChecklistItemRequest(
    bool? BooleanValue,
    decimal? NumericValue,
    string? SelectedOption,
    string? NoteText,
    Guid? AttachmentId);

internal sealed record OpenDowntimeIntervalRequest(DowntimeClassification Classification);

internal sealed record CloseDowntimeIntervalRequest(DowntimeCauseCategory CauseCategory, string CauseMechanism);

internal sealed record PostPartUsageRequest(
    string PartName,
    string? PartCode,
    decimal Quantity,
    decimal UnitCost,
    string Currency,
    string? IdempotencyKey);

// ---------- Responses ----------

internal sealed record ChecklistItemResponse(
    Guid Id,
    Guid WorkOrderId,
    int ExecutionCycle,
    int SortOrder,
    ChecklistItemType ItemType,
    string Label,
    bool IsRequired,
    bool SafetyCritical,
    decimal? NumericMinValue,
    decimal? NumericMaxValue,
    string? NumericUnit,
    string? SingleSelectOptionsCsv,
    bool IsResolved,
    bool? BooleanValue,
    decimal? NumericValue,
    string? SelectedOption,
    string? NoteText,
    Guid? AttachmentId,
    bool? NumericOutOfTolerance,
    DateTimeOffset? ResolvedAtUtc,
    Guid? ResolvedByUserId)
{
    public static ChecklistItemResponse From(ChecklistItem item) => new(
        item.Id, item.WorkOrderId, item.ExecutionCycle, item.SortOrder, item.ItemType, item.Label, item.IsRequired,
        item.SafetyCritical, item.NumericMinValue, item.NumericMaxValue, item.NumericUnit, item.SingleSelectOptionsCsv,
        item.IsResolved, item.BooleanValue, item.NumericValue, item.SelectedOption, item.NoteText, item.AttachmentId,
        item.NumericOutOfTolerance, item.ResolvedAtUtc, item.ResolvedByUserId);
}

internal sealed record DowntimeIntervalResponse(
    Guid Id,
    Guid WorkOrderId,
    Guid AssetId,
    int ExecutionCycle,
    DowntimeClassification Classification,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    DowntimeCauseCategory? CauseCategory,
    string? CauseMechanism,
    Guid RecordedByUserId)
{
    public static DowntimeIntervalResponse From(DowntimeInterval interval) => new(
        interval.Id, interval.WorkOrderId, interval.AssetId, interval.ExecutionCycle, interval.Classification,
        interval.StartedAtUtc, interval.EndedAtUtc, interval.CauseCategory, interval.CauseMechanism, interval.RecordedByUserId);
}

internal sealed record PartUsageResponse(
    Guid Id,
    Guid WorkOrderId,
    int ExecutionCycle,
    string PartName,
    string? PartCode,
    decimal Quantity,
    decimal? UnitCost,
    string? Currency,
    DateTimeOffset CreatedAtUtc)
{
    /// <summary>Costs are masked to null for a caller without <see
    /// cref="PermissionCatalog.CostsView"/> (Technician isn't seeded that permission) — same
    /// field-level masking pattern as Requester's "limited_asset_fields" elsewhere in this
    /// codebase, not a separate endpoint.</summary>
    public static PartUsageResponse From(PartUsage usage, bool canViewCosts) => new(
        usage.Id, usage.WorkOrderId, usage.ExecutionCycle, usage.PartName, usage.PartCode, usage.Quantity,
        canViewCosts ? usage.UnitCost : null, canViewCosts ? usage.Currency : null, usage.CreatedAtUtc);
}
