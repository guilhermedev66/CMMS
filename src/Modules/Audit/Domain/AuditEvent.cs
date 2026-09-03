namespace Cmms.Modules.Audit.Domain;

/// <summary>
/// One append-only audit row, per docs/02-security-and-invariants.md § "Audit trail":
/// event_id, occurred_at, actor_user_id, action, resource_type/resource_id, site_id,
/// correlation_id, an explicit reason (for cancellation/hold/close-override/reopen/
/// criticality-change/privileged correction), and a selective before/after payload —
/// never a full entity dump, never secrets/attachment content.
///
/// This table is never updated or deleted by application code after insert (no mutator
/// methods are exposed on this type). See <see cref="Infrastructure.AuditDbContext"/> for the
/// note on the runtime DB role restriction this still owes.
/// </summary>
public sealed class AuditEvent
{
    private AuditEvent()
    {
    }

    public AuditEvent(
        Guid? actorUserId,
        string action,
        string resourceType,
        Guid resourceId,
        Guid? siteId,
        Guid? correlationId,
        string? reason,
        string? beforeJson,
        string? afterJson)
    {
        if (string.IsNullOrWhiteSpace(action))
        {
            throw new ArgumentException("Action is required.", nameof(action));
        }

        if (string.IsNullOrWhiteSpace(resourceType))
        {
            throw new ArgumentException("Resource type is required.", nameof(resourceType));
        }

        Id = Guid.CreateVersion7();
        OccurredAtUtc = DateTimeOffset.UtcNow;
        ActorUserId = actorUserId;
        Action = action;
        ResourceType = resourceType;
        ResourceId = resourceId;
        SiteId = siteId;
        CorrelationId = correlationId;
        Reason = reason;
        BeforeJson = beforeJson;
        AfterJson = afterJson;
    }

    public Guid Id { get; private set; }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    /// <summary>Null for a service/system identity acting without a human actor.</summary>
    public Guid? ActorUserId { get; private set; }

    /// <summary>Dotted action code, e.g. "asset.criticality.changed".</summary>
    public string Action { get; private set; } = string.Empty;

    public string ResourceType { get; private set; } = string.Empty;

    public Guid ResourceId { get; private set; }

    public Guid? SiteId { get; private set; }

    public Guid? CorrelationId { get; private set; }

    public string? Reason { get; private set; }

    /// <summary>Selective before-state fields as a JSON object, never a full entity dump.</summary>
    public string? BeforeJson { get; private set; }

    /// <summary>Selective after-state fields as a JSON object, never a full entity dump.</summary>
    public string? AfterJson { get; private set; }
}
