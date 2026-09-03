namespace Cmms.Modules.MaintenanceRequests.Domain;

/// <summary>
/// The intake entity, per docs/01-domain-and-workflows.md § "Corrective maintenance flow".
/// Site-bound at creation (docs/01 § Site-boundness: <see cref="SiteId"/> is set once and never
/// updated — enforced again at the DB level by a BEFORE UPDATE trigger, same pattern as
/// assets.assets/assets.locations).
///
/// <see cref="Status"/>'s three terminal transitions (Convert/Reject/Cancel) are deliberately
/// **not** exposed as mutator methods here. Per docs/01's "Resolves QA finding B-04(1)" note and
/// docs/02's concurrency table, each transition is implemented by the endpoint as a single
/// conditional `UPDATE ... WHERE status = 'New'` (see
/// src/Cmms.Api/MaintenanceRequestsEndpoints.cs) — an atomic, race-safe database statement, not a
/// read-then-write domain method. The entity is reloaded after that statement for the response
/// projection; modeling the transition as a C# method here would invite exactly the read-then-write
/// race the docs call out.
/// </summary>
public sealed class MaintenanceRequest
{
    private MaintenanceRequest()
    {
    }

    public MaintenanceRequest(
        Guid siteId,
        Guid createdByUserId,
        string title,
        string? description,
        Guid? assetId,
        Guid? locationId,
        RequestPriority priority = RequestPriority.P3)
    {
        Id = Guid.CreateVersion7();
        SiteId = siteId;
        CreatedByUserId = createdByUserId;
        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        AssetId = assetId;
        LocationId = locationId;
        Priority = priority;
        Status = MaintenanceRequestStatus.New;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        RowVersion = 1;
    }

    public Guid Id { get; private set; }

    public Guid SiteId { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>Target asset, if known. Mutually informative with <see cref="LocationId"/> — at
    /// least one must be present (validated at the endpoint, per docs/01: "submitted ... against an
    /// asset or a location (if the asset is unknown)").</summary>
    public Guid? AssetId { get; private set; }

    public Guid? LocationId { get; private set; }

    public RequestPriority Priority { get; private set; }

    public MaintenanceRequestStatus Status { get; private set; }

    /// <summary>Unique (enforced by DB index), per docs/01: "converted_work_order_id on the
    /// Request and source_request_id on the Work Order are both unique."</summary>
    public Guid? ConvertedWorkOrderId { get; private set; }

    public string? RejectedReason { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>Set (by the same conditional UPDATE) when the request leaves New, regardless of
    /// which of the three resolutions it took.</summary>
    public DateTimeOffset? ResolvedAtUtc { get; private set; }

    public long RowVersion { get; private set; }
}
