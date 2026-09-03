namespace Cmms.Modules.WorkManagement.Domain;

/// <summary>
/// Thrown when a transition method is invoked against a Work Order whose current
/// <see cref="WorkOrder.Status"/> does not permit it. Endpoints are expected to load the row under
/// <c>SELECT ... FOR UPDATE</c> and check status themselves before calling a mutator (per
/// docs/02-security-and-invariants.md's general concurrency protocol), so this is a defense-in-depth
/// guard, not the primary mechanism — it should never fire in normal operation.
/// </summary>
public sealed class InvalidWorkOrderTransitionException(string message) : InvalidOperationException(message);

/// <summary>
/// Per docs/01-domain-and-workflows.md § "Work Order lifecycle" and its transition table. Created
/// either directly (<see cref="WorkOrdersCreate"/> permission) or via Request conversion
/// (<see cref="SourceRequestId"/> set, unique — "one WO per request").
///
/// Self-claim is deliberately **not** a method here (see src/Cmms.Api/WorkOrdersEndpoints.cs): it
/// is the one transition in this codebase implemented as a raw, atomic conditional
/// <c>UPDATE ... WHERE assignee_id IS NULL AND status = 'Open'</c>, exactly per docs/02's concrete
/// example — not a read-then-write domain method, because that read-then-write shape is the exact
/// race this method deliberately avoids.
/// </summary>
public sealed class WorkOrder
{
    private WorkOrder()
    {
    }

    public WorkOrder(
        Guid siteId,
        string title,
        string? description,
        Guid? assetId,
        Guid? locationId,
        Guid createdByUserId,
        WorkOrderPriority priority = WorkOrderPriority.P3,
        Guid? sourceRequestId = null)
    {
        Id = Guid.CreateVersion7();
        SiteId = siteId;
        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        AssetId = assetId;
        LocationId = locationId;
        CreatedByUserId = createdByUserId;
        Priority = priority;
        SourceRequestId = sourceRequestId;
        Status = WorkOrderStatus.Draft;
        ExecutionCycle = 1;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        RowVersion = 1;
    }

    public Guid Id { get; private set; }

    public Guid SiteId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public Guid? AssetId { get; private set; }

    public Guid? LocationId { get; private set; }

    public WorkOrderStatus Status { get; private set; }

    public Guid? AssigneeId { get; private set; }

    public DateTimeOffset? AssignedAtUtc { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public WorkOrderPriority Priority { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>Set once per execution cycle by Start Work; overwritten on the next Reopen ->
    /// Start-of-work-again. Per this slice's scope cut (no per-cycle child tables — see
    /// <see cref="Reopen"/>), only the latest cycle's timestamp is retained.</summary>
    public DateTimeOffset? WrenchStartAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public Guid? CompletedByUserId { get; private set; }

    public DateTimeOffset? ClosedAtUtc { get; private set; }

    public Guid? ClosedByUserId { get; private set; }

    public DateTimeOffset? CancelledAtUtc { get; private set; }

    public Guid? CancelledByUserId { get; private set; }

    public string? CancelReason { get; private set; }

    public string? ReopenReason { get; private set; }

    /// <summary>Starts at 1; incremented by <see cref="Reopen"/>. Per docs/01: "All
    /// execution-scoped child data ... is keyed by (work_order_id, execution_cycle)." This bounded
    /// slice has no checklist/labor/downtime child tables, so nothing is actually keyed by this
    /// value yet — it is carried on the aggregate now so those tables can adopt it later without a
    /// breaking migration, per this slice's documented scope cut.</summary>
    public int ExecutionCycle { get; private set; }

    /// <summary>Unique (enforced by DB index) — at most one Work Order per source Request.</summary>
    public Guid? SourceRequestId { get; private set; }

    public long RowVersion { get; private set; }

    /// <summary>Draft -> Open. Mapped to the <c>workorders.plan</c> permission (the catalog has no
    /// dedicated "publish" permission; "ready this order for scheduling" is the same atomic
    /// operation the Planner role's plan authority already covers).</summary>
    public void Publish()
    {
        RequireStatus(WorkOrderStatus.Draft, nameof(Publish));
        Status = WorkOrderStatus.Open;
    }

    /// <summary>Scheduled -> InProgress ("Start Work").</summary>
    public void StartWork()
    {
        RequireStatus(WorkOrderStatus.Scheduled, nameof(StartWork));
        Status = WorkOrderStatus.InProgress;
        WrenchStartAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>InProgress -> Completed. Enforces docs/01's completion guard for the two child
    /// tables this slice built (checklist, downtime) — the caller (see
    /// src/Cmms.Api/WorkOrdersEndpoints.cs) computes both booleans from the current execution
    /// cycle's child rows under the same root lock, so this method never queries the database
    /// itself; it only enforces the invariant once the facts are known.
    ///
    /// SCOPE CUT, carried forward and narrowed from the prior version of this comment: docs/01 also
    /// lists "≥1 labor entry" in the same guard. This slice has no per-entry labor ledger — only the
    /// single <see cref="WrenchStartAtUtc"/> timestamp set by <see cref="StartWork"/> (asserted
    /// non-null defensively below, since it should always be set by the time a Work Order reaches
    /// InProgress) — so "labor recorded" degrades to "work was started", not "at least one itemized
    /// labor entry exists". Building a labor ledger is deferred, same bounded-slice pattern as parts
    /// (record-only ledger) and checklist (no template CRUD).</summary>
    public void MarkCompleted(
        Guid completedByUserId,
        bool allRequiredChecklistItemsResolved,
        bool hasOpenDowntimeInterval)
    {
        RequireStatus(WorkOrderStatus.InProgress, nameof(MarkCompleted));

        if (WrenchStartAtUtc is null)
        {
            throw new InvalidWorkOrderTransitionException(
                "Mark Completed requires work to have been started (no wrench-start timestamp recorded).");
        }

        if (!allRequiredChecklistItemsResolved)
        {
            throw new InvalidWorkOrderTransitionException(
                "Mark Completed requires every required checklist item for this execution cycle to be resolved.");
        }

        if (hasOpenDowntimeInterval)
        {
            throw new InvalidWorkOrderTransitionException(
                "Mark Completed requires every downtime interval for this execution cycle to be closed with a cause.");
        }

        Status = WorkOrderStatus.Completed;
        CompletedAtUtc = DateTimeOffset.UtcNow;
        CompletedByUserId = completedByUserId;
    }

    /// <summary>Completed -> Closed.</summary>
    public void Close(Guid closedByUserId)
    {
        RequireStatus(WorkOrderStatus.Completed, nameof(Close));
        Status = WorkOrderStatus.Closed;
        ClosedAtUtc = DateTimeOffset.UtcNow;
        ClosedByUserId = closedByUserId;
    }

    /// <summary>Draft/Open/Scheduled/InProgress -> Cancelled. Reason required per docs/02's audit
    /// trail minimum ("an explicit reason for cancellation").</summary>
    public void Cancel(Guid cancelledByUserId, string reason)
    {
        if (Status is not (WorkOrderStatus.Draft or WorkOrderStatus.Open or WorkOrderStatus.Scheduled or WorkOrderStatus.InProgress))
        {
            throw new InvalidWorkOrderTransitionException(
                $"Cancel is not valid from status {Status}.");
        }

        Status = WorkOrderStatus.Cancelled;
        CancelledAtUtc = DateTimeOffset.UtcNow;
        CancelledByUserId = cancelledByUserId;
        CancelReason = reason;
    }

    /// <summary>Completed/Closed -> InProgress, incrementing <see cref="ExecutionCycle"/>. Reason
    /// required per docs/01's transition table.</summary>
    public void Reopen(string reason)
    {
        if (Status is not (WorkOrderStatus.Completed or WorkOrderStatus.Closed))
        {
            throw new InvalidWorkOrderTransitionException(
                $"Reopen is not valid from status {Status}.");
        }

        Status = WorkOrderStatus.InProgress;
        ExecutionCycle += 1;
        ReopenReason = reason;
    }

    private void RequireStatus(WorkOrderStatus required, string operation)
    {
        if (Status != required)
        {
            throw new InvalidWorkOrderTransitionException(
                $"{operation} requires status {required}, but the Work Order is {Status}.");
        }
    }
}
