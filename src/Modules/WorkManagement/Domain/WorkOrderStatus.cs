namespace Cmms.Modules.WorkManagement.Domain;

/// <summary>
/// Subset of docs/01-domain-and-workflows.md § "Work Order lifecycle" implemented in this slice:
/// Draft -> Open -> Scheduled -> InProgress -> Completed -> Closed, plus Cancelled (reachable from
/// Draft/Open/Scheduled/InProgress) and Reopen (Completed/Closed -> InProgress).
///
/// SCOPE CUT: <c>OnHold</c> is deferred to a follow-up, per this task's explicit allowance — no
/// endpoint or state claims to support it. Reschedule/Reassign/Unassign (Planner-driven changes to
/// an already-Scheduled order) and Planner-driven direct Assign are also deferred: this slice's
/// only path from Open to Scheduled is the self-claim endpoint, which is the one the flagship
/// concurrency test targets. Both cuts are noted here rather than silently unimplemented.
/// </summary>
public enum WorkOrderStatus
{
    Draft,
    Open,
    Scheduled,
    InProgress,
    Completed,
    Closed,
    Cancelled
}

/// <summary>
/// docs/01 doesn't spell out a priority scale (only docs/02's permission catalog names a
/// <c>workorders.prioritize</c> operation, without defining levels), so this is a formalization of
/// the P1-P4 scale docs/04-frontend-ia.md's color-token table anchors ("P1 / Emergency", "P2 /
/// Warning") — not an invented addition. SCOPE CUT: set once at creation, no dedicated
/// "reprioritize" endpoint yet (workorders.prioritize permission exists in the catalog but nothing
/// calls it) — same bounded-slice pattern as this file's other cuts.
/// </summary>
public enum WorkOrderPriority
{
    P1,
    P2,
    P3,
    P4
}
