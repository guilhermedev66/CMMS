using Cmms.Modules.Audit.Domain;
using Cmms.Modules.Audit.Infrastructure;

namespace Cmms.Modules.Audit.Application;

/// <summary>
/// Payload for one audit row. Mirrors <see cref="AuditEvent"/>'s constructor — kept as a
/// separate record so callers in other modules depend only on this application-layer contract,
/// not on the Audit module's EF entity.
/// </summary>
public sealed record AuditEventEntry(
    Guid? ActorUserId,
    string Action,
    string ResourceType,
    Guid ResourceId,
    Guid? SiteId,
    Guid? CorrelationId,
    string? Reason,
    string? BeforeJson,
    string? AfterJson);

/// <summary>
/// Minimal application-layer service other modules call to write an audit event.
///
/// This does not open its own transaction — the caller passes in an <see cref="AuditDbContext"/>
/// that was created against a <see cref="Cmms.BuildingBlocks.Database.SharedTransactionScope"/>
/// shared with the module making the domain change, so the audit insert and that domain change's
/// SaveChanges commit or roll back together (docs/02-security-and-invariants.md § "Audit trail":
/// "Written in the same transaction as the domain change ... not bolted on afterward").
/// </summary>
public interface IAuditEventWriter
{
    Task WriteAsync(AuditDbContext auditContext, AuditEventEntry entry, CancellationToken cancellationToken = default);
}

public sealed class AuditEventWriter : IAuditEventWriter
{
    public async Task WriteAsync(
        AuditDbContext auditContext,
        AuditEventEntry entry,
        CancellationToken cancellationToken = default)
    {
        var auditEvent = new AuditEvent(
            entry.ActorUserId,
            entry.Action,
            entry.ResourceType,
            entry.ResourceId,
            entry.SiteId,
            entry.CorrelationId,
            entry.Reason,
            entry.BeforeJson,
            entry.AfterJson);

        auditContext.AuditEvents.Add(auditEvent);
        await auditContext.SaveChangesAsync(cancellationToken);
    }
}
