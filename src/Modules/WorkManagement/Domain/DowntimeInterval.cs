namespace Cmms.Modules.WorkManagement.Domain;

/// <summary>Per docs/01-domain-and-workflows.md § "Downtime tracking".</summary>
public enum DowntimeClassification
{
    FullStop,
    PartialDerating
}

public enum DowntimeCauseCategory
{
    Mechanical,
    Electrical,
    Hydraulic,
    Pneumatic,
    Instrumentation,
    Operational
}

public sealed class InvalidDowntimeIntervalOperationException(string message) : InvalidOperationException(message);

/// <summary>
/// One downtime interval against an asset, opened/closed within a Work Order's execution. Per
/// docs/01: "Not a single mutable total — a set of intervals per Work Order/Asset." A
/// <see cref="DowntimeClassification.FullStop"/> interval can never overlap another FullStop
/// interval for the same asset — enforced by a PostgreSQL exclusion constraint (see
/// WorkManagementDbContext's migration), not just this type. <see cref="DowntimeClassification.PartialDerating"/>
/// intervals are allowed to overlap by design (docs/01: "two lines can be derated in parallel") and
/// carry no such constraint.
/// </summary>
public sealed class DowntimeInterval
{
    private DowntimeInterval()
    {
    }

    public DowntimeInterval(
        Guid workOrderId,
        Guid siteId,
        Guid assetId,
        int executionCycle,
        DowntimeClassification classification,
        Guid recordedByUserId)
    {
        Id = Guid.CreateVersion7();
        WorkOrderId = workOrderId;
        SiteId = siteId;
        AssetId = assetId;
        ExecutionCycle = executionCycle;
        Classification = classification;
        StartedAtUtc = DateTimeOffset.UtcNow;
        RecordedByUserId = recordedByUserId;
    }

    public Guid Id { get; private set; }

    public Guid WorkOrderId { get; private set; }

    public Guid SiteId { get; private set; }

    public Guid AssetId { get; private set; }

    public int ExecutionCycle { get; private set; }

    public DowntimeClassification Classification { get; private set; }

    public DateTimeOffset StartedAtUtc { get; private set; }

    public DateTimeOffset? EndedAtUtc { get; private set; }

    public DowntimeCauseCategory? CauseCategory { get; private set; }

    /// <summary>Free-text second-level cause code (docs/01 lists an illustrative, non-exhaustive
    /// set — Wear/Contamination/ThermalOverload/... — "etc.", so this isn't a closed enum).</summary>
    public string? CauseMechanism { get; private set; }

    public Guid RecordedByUserId { get; private set; }

    /// <summary>
    /// Ends the interval with a required cause code — docs/01: "A corrective Work Order that
    /// represents a machine-down event cannot close without started_at/ended_at and a cause code
    /// recorded."
    /// </summary>
    public void Close(DowntimeCauseCategory causeCategory, string causeMechanism)
    {
        if (EndedAtUtc is not null)
        {
            throw new InvalidDowntimeIntervalOperationException("This downtime interval is already closed.");
        }

        if (string.IsNullOrWhiteSpace(causeMechanism))
        {
            throw new InvalidDowntimeIntervalOperationException("A cause mechanism is required to close a downtime interval.");
        }

        EndedAtUtc = DateTimeOffset.UtcNow;
        CauseCategory = causeCategory;
        CauseMechanism = causeMechanism.Trim();
    }

    /// <summary>System-generated close (docs/02: an open interval is force-closed with a system
    /// note when the owning Work Order is Cancelled, or Reopen starts a new cycle over a still-open
    /// prior interval).</summary>
    public void ForceCloseAsSystem()
    {
        if (EndedAtUtc is not null)
        {
            return;
        }

        EndedAtUtc = DateTimeOffset.UtcNow;
        CauseMechanism = "System-closed: Work Order left this execution cycle with the interval still open.";
    }
}
