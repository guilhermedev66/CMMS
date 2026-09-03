using System.Security.Claims;
using System.Text.Json;
using Cmms.Api.Realtime;
using Cmms.BuildingBlocks.Database;
using Cmms.Modules.Assets.Infrastructure;
using Cmms.Modules.Audit.Application;
using Cmms.Modules.Audit.Infrastructure;
using Cmms.Modules.IdentityAccess.Application;
using Cmms.Modules.IdentityAccess.Domain;
using Cmms.Modules.PreventiveMaintenance.Domain;
using Cmms.Modules.PreventiveMaintenance.Infrastructure;
using Cmms.Modules.WorkManagement.Domain;
using Cmms.Modules.WorkManagement.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace Cmms.Api;

/// <summary>
/// Work Order lifecycle, per docs/01-domain-and-workflows.md § "Work Order lifecycle" and
/// docs/02-security-and-invariants.md's atomic permission table (<c>workorders.*</c> rows).
///
/// SCOPE CUT (carried over from <see cref="WorkOrder"/> and <see cref="WorkOrderStatus"/>'s own
/// doc comments): this slice implements Draft -> Open -> Scheduled(self-claim only) -> InProgress
/// -> Completed -> Closed, plus Cancel and Reopen. No <c>OnHold</c>, no Planner-driven
/// Assign/Reassign/Unassign, no Reschedule — those endpoints simply don't exist yet. This is the
/// bounded slice the flagship concurrent-self-claim test (docs/02 § Concurrency & invariants)
/// actually needs; the frontend's transition menu is scoped to match (see
/// web/src/mocks/workOrders.ts).
///
/// "Start Work" and "Mark Completed" are actor-gated as "assignee, or Planner/Admin" per docs/01's
/// transition table. <see cref="PermissionCatalog.WorkOrdersExecute"/>/<see
/// cref="PermissionCatalog.WorkOrdersComplete"/> only check permission + site (Planner/Admin's own
/// site, or a Technician's site membership) — they don't know about a specific Work Order's
/// assignee, so the "own assignment" half of a Technician's grant is enforced explicitly below
/// (<see cref="RequireAssigneeOrPlannerAsync"/>), the same way AssetsEndpoints enforces
/// same-site predicates HasPermissionAsync alone can't express.
/// </summary>
internal static class WorkOrdersEndpoints
{
    public static IEndpointRouteBuilder MapWorkOrdersEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var workOrders = endpoints.MapGroup("/work-orders").WithTags("WorkOrders").RequireAuthorization();
        workOrders.MapGet("", ListWorkOrdersAsync);
        workOrders.MapGet("/{id:guid}", GetWorkOrderAsync);
        workOrders.MapPost("", CreateWorkOrderAsync);
        workOrders.MapPost("/{id:guid}/publish", PublishAsync);
        workOrders.MapPost("/{id:guid}/self-claim", SelfClaimAsync);
        workOrders.MapPost("/{id:guid}/start", StartWorkAsync);
        workOrders.MapPost("/{id:guid}/complete", CompleteAsync);
        workOrders.MapPost("/{id:guid}/close", CloseAsync);
        workOrders.MapPost("/{id:guid}/reopen", ReopenAsync);
        workOrders.MapPost("/{id:guid}/cancel", CancelAsync);

        return endpoints;
    }

    // ---------- Reads ----------

    private static async Task<IResult> ListWorkOrdersAsync(
        Guid? siteId,
        Guid? assetId,
        ClaimsPrincipal user,
        WorkManagementDbContext workOrdersDb,
        IPermissionEvaluator permissions,
        CancellationToken cancellationToken)
    {
        var userId = permissions.GetUserId(user);
        var readAll = await permissions.GetSiteScopeAsync(user, PermissionCatalog.WorkOrdersReadAll, cancellationToken);
        var readAssigned = await permissions.GetSiteScopeAsync(user, PermissionCatalog.WorkOrdersReadAssigned, cancellationToken);

        if (userId is null || (!readAll.AllSites && readAll.SiteIds.Count == 0 && readAssigned.SiteIds.Count == 0))
        {
            return Results.Ok(Array.Empty<WorkOrderResponse>());
        }

        IQueryable<WorkOrder> query = workOrdersDb.WorkOrders.AsNoTracking();
        if (!readAll.AllSites)
        {
            var fullVisibilitySiteIds = readAll.SiteIds;
            var ownAssignmentSiteIds = readAssigned.SiteIds;
            query = query.Where(workOrder =>
                fullVisibilitySiteIds.Contains(workOrder.SiteId) ||
                (ownAssignmentSiteIds.Contains(workOrder.SiteId) && workOrder.AssigneeId == userId));
        }

        if (siteId is not null)
        {
            if (!readAll.Includes(siteId.Value) && !readAssigned.Includes(siteId.Value))
            {
                return Results.Ok(Array.Empty<WorkOrderResponse>());
            }

            query = query.Where(workOrder => workOrder.SiteId == siteId.Value);
        }

        // Purely a narrowing filter on top of the RBAC-scoped query above — it grants no
        // additional visibility. This is what the QR asset deep-link (docs/02 § "QR strategy")
        // uses: scanning a tag never bypasses workorders.read.* visibility, it just asks the same
        // authorized query for the subset tied to one asset.
        if (assetId is not null)
        {
            query = query.Where(workOrder => workOrder.AssetId == assetId.Value);
        }

        var list = await query.OrderByDescending(workOrder => workOrder.CreatedAtUtc).ToListAsync(cancellationToken);
        return Results.Ok(list.Select(WorkOrderResponse.From));
    }

    private static async Task<IResult> GetWorkOrderAsync(
        Guid id,
        ClaimsPrincipal user,
        WorkManagementDbContext workOrdersDb,
        IPermissionEvaluator permissions,
        CancellationToken cancellationToken)
    {
        var workOrder = await workOrdersDb.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (workOrder is null)
        {
            return Results.NotFound();
        }

        var userId = permissions.GetUserId(user);
        var canReadAll = await permissions.HasPermissionAsync(user, PermissionCatalog.WorkOrdersReadAll, workOrder.SiteId, cancellationToken);
        var canReadAssigned = userId == workOrder.AssigneeId &&
            await permissions.HasPermissionAsync(user, PermissionCatalog.WorkOrdersReadAssigned, workOrder.SiteId, cancellationToken);

        if (!canReadAll && !canReadAssigned)
        {
            return Results.NotFound();
        }

        return Results.Ok(WorkOrderResponse.From(workOrder));
    }

    // ---------- Create / Publish ----------

    private static async Task<IResult> CreateWorkOrderAsync(
        CreateWorkOrderRequest request,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        WorkManagementDbContext workOrdersDb,
        AssetsDbContext assetsDb,
        IPermissionEvaluator permissions,
        CancellationToken cancellationToken)
    {
        if (!await AntiforgeryHelpers.HasValidAntiforgeryTokenAsync(httpContext, antiforgery))
        {
            return Results.BadRequest(new { error = "Invalid anti-forgery token." });
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["title"] = ["Title is required."]
            });
        }

        if (!await permissions.HasPermissionAsync(httpContext.User, PermissionCatalog.WorkOrdersCreate, request.SiteId, cancellationToken))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Not permitted to create Work Orders at this site.");
        }

        if (request.AssetId is not null)
        {
            var assetInSameSite = await assetsDb.Assets
                .AnyAsync(asset => asset.Id == request.AssetId && asset.SiteId == request.SiteId, cancellationToken);
            if (!assetInSameSite)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["assetId"] = ["Asset must belong to the same site."]
                });
            }
        }

        if (request.LocationId is not null)
        {
            var locationInSameSite = await assetsDb.Locations
                .AnyAsync(location => location.Id == request.LocationId && location.SiteId == request.SiteId, cancellationToken);
            if (!locationInSameSite)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["locationId"] = ["Location must belong to the same site."]
                });
            }
        }

        var actorUserId = permissions.GetUserId(httpContext.User)
            ?? throw new InvalidOperationException("Authenticated request is missing a user id claim.");

        var workOrder = new WorkOrder(
            request.SiteId,
            request.Title,
            request.Description,
            request.AssetId,
            request.LocationId,
            actorUserId,
            request.Priority);

        workOrdersDb.WorkOrders.Add(workOrder);
        await workOrdersDb.SaveChangesAsync(cancellationToken);

        // Deliberately NOT broadcast here. A newly created order is still Draft — Planner/Admin-only
        // internal planning, not yet visible to a Technician through the ordinary REST API (nothing
        // in workorders.read.assigned's scope shows an unassigned Draft order). The dispatch-board
        // hub's group is site-wide (every active member of the site, any role — see
        // WorkOrderDispatchHub's doc comment on that being an intentional "shared board" design,
        // not per-assignment scoping), so broadcasting at Draft creation would leak the existence
        // and asset-targeting of unpublished planning activity to every Technician at the site —
        // strictly broader than what the REST API would ever show them. TransitionAsync's Publish
        // broadcast (the point the order actually becomes visible/actionable) is the real "this
        // exists now" signal, P1 alert included; this method deliberately doesn't duplicate it.
        return Results.Created($"/work-orders/{workOrder.Id}", WorkOrderResponse.From(workOrder));
    }

    private static Task<IResult> PublishAsync(
        Guid id,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        IConfiguration configuration,
        IPermissionEvaluator permissions,
        IAuditEventWriter auditWriter,
        WorkOrderDispatchBroadcaster broadcaster,
        CancellationToken cancellationToken) =>
        TransitionAsync(
            id,
            httpContext,
            antiforgery,
            configuration,
            permissions,
            auditWriter,
            broadcaster,
            PermissionCatalog.WorkOrdersPlan,
            actorCheck: null,
            action: "workorder.published",
            reason: null,
            mutate: workOrder => workOrder.Publish(),
            cancellationToken: cancellationToken);

    // ---------- Self-claim: the flagship concurrency-protected transition ----------

    /// <summary>
    /// Not a domain method — a single atomic conditional <c>UPDATE</c>, exactly per
    /// docs/02-security-and-invariants.md's concrete example: "Two technicians claim the same Work
    /// Order" is resolved by "Atomic conditional UPDATE ... WHERE assignee IS NULL AND status IN
    /// (...) — not read-then-write". A read-then-check-then-write here would reopen exactly the
    /// race this endpoint exists to close.
    /// </summary>
    private static async Task<IResult> SelfClaimAsync(
        Guid id,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        IConfiguration configuration,
        IPermissionEvaluator permissions,
        IAuditEventWriter auditWriter,
        WorkOrderDispatchBroadcaster broadcaster,
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

        var workOrder = await workOrdersDb.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, cancellationToken);
        if (workOrder is null)
        {
            return Results.NotFound();
        }

        var actorUserId = permissions.GetUserId(httpContext.User);
        if (actorUserId is null || !await permissions.HasPermissionAsync(httpContext.User, PermissionCatalog.WorkOrdersSelfClaim, workOrder.SiteId, cancellationToken))
        {
            return Results.NotFound();
        }

        // The permission/site check above is a pre-check for a clean 403/404 in the common case;
        // this UPDATE's own WHERE clause is the actual race-safe authorization boundary — it
        // re-validates site and unassigned/Open status atomically against whichever row a
        // concurrent racer might just have won.
        var affectedRows = await workOrdersDb.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE work_management.work_orders
             SET assignee_id = {actorUserId}, assigned_at_utc = now(), status = 'Scheduled', row_version = row_version + 1
             WHERE id = {id} AND site_id = {workOrder.SiteId} AND assignee_id IS NULL AND status = 'Open'
             """,
            cancellationToken);

        if (affectedRows == 0)
        {
            await transactionScope.RollbackAsync(cancellationToken);
            return Results.Conflict(new { error = "This Work Order is already claimed or is no longer claimable." });
        }

        // Read the post-UPDATE row while the transaction/connection is still open (a transaction's
        // own uncommitted writes are visible to later reads on the same connection) — a DbContext
        // bound to this transactionScope can no longer be used at all once CommitAsync below
        // completes it, so this read must happen before that point, not after.
        var updated = await workOrdersDb.WorkOrders.AsNoTracking().FirstAsync(w => w.Id == id, cancellationToken);

        await auditWriter.WriteAsync(
            auditDb,
            new AuditEventEntry(
                ActorUserId: actorUserId,
                Action: "workorder.selfclaimed",
                ResourceType: "WorkOrder",
                ResourceId: id,
                SiteId: workOrder.SiteId,
                CorrelationId: null,
                Reason: null,
                BeforeJson: JsonSerializer.Serialize(new { status = "Open", assigneeId = (Guid?)null }),
                AfterJson: JsonSerializer.Serialize(new { status = "Scheduled", assigneeId = actorUserId })),
            cancellationToken);

        await transactionScope.CommitAsync(cancellationToken);

        await broadcaster.WorkOrderChangedAsync(
            updated.SiteId,
            new WorkOrderChangedPayload(updated.Id, updated.SiteId, updated.Status.ToString(), updated.Priority.ToString(), updated.AssetId, "selfclaimed"),
            cancellationToken);

        return Results.Ok(WorkOrderResponse.From(updated));
    }

    // ---------- Execution ----------

    private static Task<IResult> StartWorkAsync(
        Guid id,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        IConfiguration configuration,
        IPermissionEvaluator permissions,
        IAuditEventWriter auditWriter,
        WorkOrderDispatchBroadcaster broadcaster,
        CancellationToken cancellationToken) =>
        TransitionAsync(
            id,
            httpContext,
            antiforgery,
            configuration,
            permissions,
            auditWriter,
            broadcaster,
            PermissionCatalog.WorkOrdersExecute,
            actorCheck: RequireAssigneeOrPlannerAsync,
            action: "workorder.started",
            reason: null,
            mutate: workOrder => workOrder.StartWork(),
            cancellationToken: cancellationToken);

    /// <summary>
    /// Not built on the shared <see cref="TransitionAsync"/> helper (unlike every other transition
    /// here) because Mark Completed's guard, per docs/01's transition table, needs to read this
    /// execution cycle's checklist/downtime child rows before deciding whether the transition is
    /// even legal — <see cref="TransitionAsync"/>'s mutator delegates only ever see the
    /// <see cref="WorkOrder"/> row itself. Same root-lock-then-authorize-then-mutate-then-audit
    /// shape, just with those two extra reads folded in under the same lock (docs/02: "Child edit
    /// ... races with completion/closure ... Both commands lock the Work Order root").
    /// </summary>
    private static async Task<IResult> CompleteAsync(
        Guid id,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        IConfiguration configuration,
        IPermissionEvaluator permissions,
        IAuditEventWriter auditWriter,
        WorkOrderDispatchBroadcaster broadcaster,
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
        if (actorUserId is null || !await permissions.HasPermissionAsync(httpContext.User, PermissionCatalog.WorkOrdersComplete, workOrder.SiteId, cancellationToken))
        {
            return Results.NotFound();
        }

        var role = await permissions.GetEffectiveRoleAsync(httpContext.User, workOrder.SiteId, cancellationToken);
        if (!RequireAssigneeOrPlannerAsync(workOrder, role, actorUserId.Value))
        {
            return Results.NotFound();
        }

        var allRequiredResolved = !await workOrdersDb.ChecklistItems.AsNoTracking().AnyAsync(
            item => item.WorkOrderId == id && item.ExecutionCycle == workOrder.ExecutionCycle &&
                item.IsRequired && !item.IsResolved,
            cancellationToken);
        var hasOpenDowntime = await workOrdersDb.DowntimeIntervals.AsNoTracking().AnyAsync(
            interval => interval.WorkOrderId == id && interval.ExecutionCycle == workOrder.ExecutionCycle &&
                interval.EndedAtUtc == null,
            cancellationToken);

        var beforeStatus = workOrder.Status;
        try
        {
            workOrder.MarkCompleted(actorUserId.Value, allRequiredResolved, hasOpenDowntime);
        }
        catch (InvalidWorkOrderTransitionException ex)
        {
            await transactionScope.RollbackAsync(cancellationToken);
            return Results.Conflict(new { error = ex.Message });
        }

        await workOrdersDb.SaveChangesAsync(cancellationToken);
        await ClearPreventiveMaintenanceOccurrenceAsync(transactionScope, workOrder, cancellationToken);

        await auditWriter.WriteAsync(
            auditDb,
            new AuditEventEntry(
                ActorUserId: actorUserId,
                Action: "workorder.completed",
                ResourceType: "WorkOrder",
                ResourceId: workOrder.Id,
                SiteId: workOrder.SiteId,
                CorrelationId: null,
                Reason: null,
                BeforeJson: JsonSerializer.Serialize(new { status = beforeStatus.ToString() }),
                AfterJson: JsonSerializer.Serialize(new { status = workOrder.Status.ToString() })),
            cancellationToken);

        await transactionScope.CommitAsync(cancellationToken);

        await broadcaster.WorkOrderChangedAsync(
            workOrder.SiteId,
            new WorkOrderChangedPayload(workOrder.Id, workOrder.SiteId, workOrder.Status.ToString(), workOrder.Priority.ToString(), workOrder.AssetId, "completed"),
            cancellationToken);

        return Results.Ok(WorkOrderResponse.From(workOrder));
    }

    private static Task<IResult> CloseAsync(
        Guid id,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        IConfiguration configuration,
        IPermissionEvaluator permissions,
        IAuditEventWriter auditWriter,
        WorkOrderDispatchBroadcaster broadcaster,
        CancellationToken cancellationToken) =>
        TransitionAsync(
            id,
            httpContext,
            antiforgery,
            configuration,
            permissions,
            auditWriter,
            broadcaster,
            PermissionCatalog.WorkOrdersClose,
            actorCheck: null,
            action: "workorder.closed",
            reason: null,
            mutateWithActor: (workOrder, actorUserId) => workOrder.Close(actorUserId),
            cancellationToken: cancellationToken);

    private static async Task<IResult> ReopenAsync(
        Guid id,
        ReasonRequest request,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        IConfiguration configuration,
        IPermissionEvaluator permissions,
        IAuditEventWriter auditWriter,
        WorkOrderDispatchBroadcaster broadcaster,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["reason"] = ["A reason is required to reopen a Work Order."]
            });
        }

        return await TransitionAsync(
            id,
            httpContext,
            antiforgery,
            configuration,
            permissions,
            auditWriter,
            broadcaster,
            PermissionCatalog.WorkOrdersReopen,
            actorCheck: null,
            action: "workorder.reopened",
            reason: request.Reason,
            mutate: workOrder => workOrder.Reopen(request.Reason),
            cancellationToken: cancellationToken);
    }

    private static async Task<IResult> CancelAsync(
        Guid id,
        ReasonRequest request,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        IConfiguration configuration,
        IPermissionEvaluator permissions,
        IAuditEventWriter auditWriter,
        WorkOrderDispatchBroadcaster broadcaster,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["reason"] = ["A reason is required to cancel a Work Order."]
            });
        }

        return await TransitionAsync(
            id,
            httpContext,
            antiforgery,
            configuration,
            permissions,
            auditWriter,
            broadcaster,
            PermissionCatalog.WorkOrdersCancel,
            actorCheck: null,
            action: "workorder.cancelled",
            reason: request.Reason,
            mutateWithActor: (workOrder, actorUserId) => workOrder.Cancel(actorUserId, request.Reason),
            cancellationToken: cancellationToken);
    }

    // ---------- Shared transition machinery ----------

    private delegate bool ActorCheck(WorkOrder workOrder, RoleCode? role, Guid actorUserId);

    private static bool RequireAssigneeOrPlannerAsync(WorkOrder workOrder, RoleCode? role, Guid actorUserId) =>
        role is RoleCode.Admin or RoleCode.Planner || workOrder.AssigneeId == actorUserId;

    /// <summary>
    /// Shared implementation for every ordinary (non-self-claim) Work Order transition: load under
    /// the root lock (<c>SELECT ... FOR UPDATE</c> via a tracked read inside the transaction —
    /// Npgsql/EF issues this as part of the update statement's row lock on commit is not
    /// sufficient by itself, so the read is re-validated by the domain method's own state guard,
    /// which throws <see cref="InvalidWorkOrderTransitionException"/> on an illegal transition,
    /// mapped to 409 here rather than a 500), authorize, run the domain mutator, write the audit
    /// event, commit — all in one transaction, per docs/02's concurrency protocol.
    /// </summary>
    private static async Task<IResult> TransitionAsync(
        Guid id,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        IConfiguration configuration,
        IPermissionEvaluator permissions,
        IAuditEventWriter auditWriter,
        WorkOrderDispatchBroadcaster broadcaster,
        string permissionCode,
        ActorCheck? actorCheck,
        string action,
        string? reason,
        Action<WorkOrder>? mutate = null,
        CancellationToken cancellationToken = default,
        Action<WorkOrder, Guid>? mutateWithActor = null)
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

        // Root-row lock, per docs/02: "begin transaction -> SELECT ... FOR UPDATE the Work Order
        // root -> authorize ... -> validate and mutate ... -> commit".
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

        var beforeStatus = workOrder.Status;
        var beforeExecutionCycle = workOrder.ExecutionCycle;
        try
        {
            if (mutateWithActor is not null)
            {
                mutateWithActor(workOrder, actorUserId.Value);
            }
            else
            {
                mutate!(workOrder);
            }
        }
        catch (InvalidWorkOrderTransitionException ex)
        {
            await transactionScope.RollbackAsync(cancellationToken);
            return Results.Conflict(new { error = ex.Message });
        }

        // docs/01: "Any open (unclosed) labor or downtime interval is force-closed with a
        // system-generated end timestamp ... when a Work Order is Cancelled, or when Reopen starts
        // a new cycle over a still-open prior interval." Left unhandled, an open FullStop interval
        // on a now-terminal (Cancelled) Work Order can never be closed through the API again
        // (CloseDowntimeIntervalAsync requires a non-terminal order) — and since the exclusion
        // constraint treats FullStop-with-no-end as occupying the asset indefinitely, that
        // permanently blocks any future FullStop interval on the same asset. Uses
        // `beforeExecutionCycle` (captured above, before Reopen's own increment) since Reopen's
        // "still-open prior interval" is in the cycle that just ended, not the new one.
        if (action is "workorder.cancelled" or "workorder.reopened")
        {
            var openIntervals = await workOrdersDb.DowntimeIntervals
                .Where(interval => interval.WorkOrderId == workOrder.Id &&
                                    interval.ExecutionCycle == beforeExecutionCycle &&
                                    interval.EndedAtUtc == null)
                .ToListAsync(cancellationToken);
            foreach (var interval in openIntervals)
            {
                interval.ForceCloseAsSystem();
            }
        }

        await workOrdersDb.SaveChangesAsync(cancellationToken);

        // docs/01: "A domain event on the generated Work Order reaching Completed, Closed, or
        // Cancelled clears active_occurrence_id back to NULL" — done here, in the same transaction
        // as the Work Order's own state change, so the plan pointer and the order's terminal state
        // change together (same requirement docs/01 states for the Reopen edge case).
        if (workOrder.Status is WorkOrderStatus.Completed or WorkOrderStatus.Closed or WorkOrderStatus.Cancelled)
        {
            await ClearPreventiveMaintenanceOccurrenceAsync(transactionScope, workOrder, cancellationToken);
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
                Reason: reason,
                BeforeJson: JsonSerializer.Serialize(new { status = beforeStatus.ToString() }),
                AfterJson: JsonSerializer.Serialize(new { status = workOrder.Status.ToString() })),
            cancellationToken);

        await transactionScope.CommitAsync(cancellationToken);

        await broadcaster.WorkOrderChangedAsync(
            workOrder.SiteId,
            new WorkOrderChangedPayload(workOrder.Id, workOrder.SiteId, workOrder.Status.ToString(), workOrder.Priority.ToString(), workOrder.AssetId, action),
            cancellationToken);
        if (action == "workorder.published" && workOrder.Priority == WorkOrderPriority.P1)
        {
            // "Published" is the moment a P1 order first becomes claimable — that's the actionable
            // emergency moment (ADR-17: "emergency high-priority alerts"), not mere creation of a
            // still-Draft order nobody can act on yet.
            await broadcaster.HighPriorityAlertAsync(
                workOrder.SiteId,
                new HighPriorityAlertPayload(workOrder.Id, workOrder.SiteId, workOrder.Title, workOrder.Priority.ToString(), workOrder.AssetId),
                cancellationToken);
        }

        return Results.Ok(WorkOrderResponse.From(workOrder));
    }

    /// <summary>
    /// No-op for an ordinary (non-preventive) Work Order. For one generated by a
    /// <see cref="MaintenancePlan"/>: clears the plan's <c>ActiveOccurrenceId</c> pointer, and for
    /// a <see cref="RecurrenceType.Floating"/> plan reaching Completed specifically, also
    /// recomputes <c>NextDueAtUtc</c> from the real completion time (docs/01). Both plan mutators
    /// are idempotent on an already-cleared pointer, so this safely no-ops on a later terminal
    /// transition of the same Work Order (e.g. Closed after Completed already cleared it).
    /// </summary>
    private static async Task ClearPreventiveMaintenanceOccurrenceAsync(
        SharedTransactionScope transactionScope,
        WorkOrder workOrder,
        CancellationToken cancellationToken)
    {
        await using var plansDb = transactionScope.CreateContext<PreventiveMaintenanceDbContext>(options => new PreventiveMaintenanceDbContext(options));

        var occurrence = await plansDb.MaintenancePlanOccurrences
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.WorkOrderId == workOrder.Id, cancellationToken);
        if (occurrence is null)
        {
            return;
        }

        var plan = await plansDb.MaintenancePlans
            .FromSqlInterpolated($"SELECT * FROM preventive_maintenance.maintenance_plans WHERE id = {occurrence.PlanId} FOR UPDATE")
            .FirstOrDefaultAsync(cancellationToken);
        if (plan is null)
        {
            return;
        }

        if (workOrder.Status == WorkOrderStatus.Completed && plan.RecurrenceType == RecurrenceType.Floating)
        {
            plan.RecordFloatingCompletion(occurrence.Id, workOrder.CompletedAtUtc ?? DateTimeOffset.UtcNow);
        }
        else
        {
            plan.ClearActiveOccurrence(occurrence.Id);
        }

        await plansDb.SaveChangesAsync(cancellationToken);
    }
}

// ---------- Requests ----------

internal sealed record CreateWorkOrderRequest(
    Guid SiteId,
    string Title,
    string? Description,
    Guid? AssetId,
    Guid? LocationId,
    WorkOrderPriority Priority = WorkOrderPriority.P3);

// ReasonRequest is defined once, in MaintenanceRequestsEndpoints.cs — same shape (a required
// reason string) is reused here for Reopen/Cancel rather than declaring a second identical record.

// ---------- Responses ----------

internal sealed record WorkOrderResponse(
    Guid Id,
    Guid SiteId,
    string Title,
    string? Description,
    Guid? AssetId,
    Guid? LocationId,
    WorkOrderStatus Status,
    WorkOrderPriority Priority,
    Guid? AssigneeId,
    DateTimeOffset? AssignedAtUtc,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? WrenchStartAtUtc,
    DateTimeOffset? CompletedAtUtc,
    Guid? CompletedByUserId,
    DateTimeOffset? ClosedAtUtc,
    Guid? ClosedByUserId,
    DateTimeOffset? CancelledAtUtc,
    string? CancelReason,
    string? ReopenReason,
    int ExecutionCycle,
    Guid? SourceRequestId,
    long RowVersion)
{
    public static WorkOrderResponse From(WorkOrder workOrder) => new(
        workOrder.Id,
        workOrder.SiteId,
        workOrder.Title,
        workOrder.Description,
        workOrder.AssetId,
        workOrder.LocationId,
        workOrder.Status,
        workOrder.Priority,
        workOrder.AssigneeId,
        workOrder.AssignedAtUtc,
        workOrder.CreatedByUserId,
        workOrder.CreatedAtUtc,
        workOrder.WrenchStartAtUtc,
        workOrder.CompletedAtUtc,
        workOrder.CompletedByUserId,
        workOrder.ClosedAtUtc,
        workOrder.ClosedByUserId,
        workOrder.CancelledAtUtc,
        workOrder.CancelReason,
        workOrder.ReopenReason,
        workOrder.ExecutionCycle,
        workOrder.SourceRequestId,
        workOrder.RowVersion);
}
