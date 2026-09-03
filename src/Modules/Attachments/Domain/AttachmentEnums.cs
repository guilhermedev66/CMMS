namespace Cmms.Modules.Attachments.Domain;

public enum AttachmentUploadIntentStatus
{
    Pending,
    Uploaded,
    Active,
    Expired,
    Rejected
}

/// <summary>
/// Bounded to Work Order evidence in this slice (docs/02-security-and-invariants.md: "narrowed for
/// v1 to bounded raster evidence photos only — checklist evidence, before/after repair photos").
/// The generic <c>parent_resource_type/parent_resource_id</c> shape docs/02 describes for Assets
/// too isn't wired to an Assets authorization path yet — deferred, not because attachments
/// couldn't point at an Asset, but because nothing in this milestone's scope needs it.
/// </summary>
public enum AttachmentParentResourceType
{
    WorkOrder
}
