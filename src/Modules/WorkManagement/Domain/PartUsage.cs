namespace Cmms.Modules.WorkManagement.Domain;

/// <summary>
/// Per docs/01-domain-and-workflows.md § "Parts & costs (lean scope)": "record-only ... an
/// immutable ledger row. No stock level tracking ... this still supports real cost-per-work-order
/// and cost-by-asset reporting." No mutator methods by design — a posting is never edited, only
/// ever inserted (a correction is a new, possibly negative-quantity, row — not built in this slice,
/// same scope-cut pattern as elsewhere: not needed to prove the ledger/idempotency invariant).
/// </summary>
public sealed class PartUsage
{
    private PartUsage()
    {
    }

    public PartUsage(
        Guid workOrderId,
        Guid siteId,
        int executionCycle,
        string partName,
        string? partCode,
        decimal quantity,
        decimal unitCost,
        string currency,
        Guid actorUserId,
        string? idempotencyKey)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Quantity must be positive.");
        }

        if (unitCost < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(unitCost), unitCost, "Unit cost cannot be negative.");
        }

        Id = Guid.CreateVersion7();
        WorkOrderId = workOrderId;
        SiteId = siteId;
        ExecutionCycle = executionCycle;
        PartName = partName.Trim();
        PartCode = string.IsNullOrWhiteSpace(partCode) ? null : partCode.Trim();
        Quantity = quantity;
        UnitCost = unitCost;
        Currency = currency.Trim().ToUpperInvariant();
        ActorUserId = actorUserId;
        IdempotencyKey = idempotencyKey;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid WorkOrderId { get; private set; }

    public Guid SiteId { get; private set; }

    public int ExecutionCycle { get; private set; }

    public string PartName { get; private set; } = string.Empty;

    public string? PartCode { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal UnitCost { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public Guid ActorUserId { get; private set; }

    /// <summary>Docs/01: "a client-supplied idempotency key deduplicates a retried insert" —
    /// unique per (work_order_id, idempotency_key) when present (see WorkManagementDbContext).</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }
}
