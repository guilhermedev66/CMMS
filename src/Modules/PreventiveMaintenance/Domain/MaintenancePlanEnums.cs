namespace Cmms.Modules.PreventiveMaintenance.Domain;

/// <summary>
/// Per docs/01-domain-and-workflows.md § "Preventive maintenance flow": meter-based and
/// condition-based triggers are explicitly out of scope for v1 — calendar-based (Fixed + Floating)
/// is the whole v1 scheduling model. Kept as its own field (rather than inferred) so meter-based
/// can be added later without a breaking migration, per that doc's explicit note.
/// </summary>
public enum RecurrenceType
{
    /// <summary>Next due date is anchored to the calendar regardless of when the prior instance
    /// closed (e.g. "every 30 days from the anchor date") — advances at generation time.</summary>
    Fixed,

    /// <summary>Next due date is calculated from the actual completion date of the prior occurrence
    /// (e.g. "30 days after last done") — only advances once that Work Order actually completes.</summary>
    Floating
}

public enum MaintenancePlanStatus
{
    Active,
    Paused
}
