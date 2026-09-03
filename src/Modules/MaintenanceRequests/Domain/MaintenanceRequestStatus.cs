namespace Cmms.Modules.MaintenanceRequests.Domain;

/// <summary>
/// Per docs/01-domain-and-workflows.md § "Corrective maintenance flow": "States: New -> Converted
/// | Rejected | Cancelled." Terminal from New — there is no path back to New from any of the three
/// resolutions.
/// </summary>
public enum MaintenanceRequestStatus
{
    New,
    Converted,
    Rejected,
    Cancelled
}

/// <summary>Same P1-P4 scale as <see cref="Cmms.Modules.WorkManagement.Domain.WorkOrderPriority"/> —
/// duplicated rather than shared across modules per this codebase's schema-per-module boundary (no
/// module references another module's Domain types). Set by the requester at intake; carried over
/// unchanged onto the created Work Order on conversion (see MaintenanceRequestsEndpoints).</summary>
public enum RequestPriority
{
    P1,
    P2,
    P3,
    P4
}
