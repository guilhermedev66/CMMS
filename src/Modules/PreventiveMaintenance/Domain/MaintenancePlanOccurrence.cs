namespace Cmms.Modules.PreventiveMaintenance.Domain;

/// <summary>
/// One generated instance of a <see cref="MaintenancePlan"/>. Per docs/01: unique
/// <c>(plan_id, scheduled_for)</c> is "the final safety net even if the lock protocol is ever
/// bypassed" — enforced by a DB index (see PreventiveMaintenanceDbContext), not just this type.
/// Immutable after creation: an occurrence records a historical fact ("this plan generated a Work
/// Order for this due date at this time"), never edited.
/// </summary>
public sealed class MaintenancePlanOccurrence
{
    private MaintenancePlanOccurrence()
    {
    }

    public MaintenancePlanOccurrence(Guid planId, Guid siteId, DateTimeOffset scheduledForUtc, Guid workOrderId)
    {
        Id = Guid.CreateVersion7();
        PlanId = planId;
        SiteId = siteId;
        ScheduledForUtc = scheduledForUtc;
        WorkOrderId = workOrderId;
        GeneratedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid PlanId { get; private set; }

    public Guid SiteId { get; private set; }

    public DateTimeOffset ScheduledForUtc { get; private set; }

    public Guid WorkOrderId { get; private set; }

    public DateTimeOffset GeneratedAtUtc { get; private set; }
}
