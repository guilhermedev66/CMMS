using Cmms.BuildingBlocks.Database;
using Cmms.Modules.Audit.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cmms.Modules.Audit.Infrastructure;

/// <summary>
/// TODO(DB-role hardening): docs/02-security-and-invariants.md's threat-model table calls for the
/// application's *runtime* DB role to have INSERT/SELECT only on audit.audit_events (no
/// UPDATE/DELETE), so the ordinary application code path cannot alter or erase existing audit
/// rows. That is a database-role/migration-pipeline concern (a distinct least-privilege runtime
/// role + GRANT/REVOKE statements applied outside of, or alongside, EF migrations) that this
/// slice does not set up — the repo currently runs everything through one migrating/owning role.
/// Flagged here deliberately rather than silently skipped; see that doc's "Trust boundary" note
/// for why this was already never meant to be a claim of tamper-proof history.
/// </summary>
public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(DatabaseSchemas.Audit);

        builder.Entity<AuditEvent>(entity =>
        {
            entity.ToTable("audit_events");
            entity.HasKey(auditEvent => auditEvent.Id);
            entity.Property(auditEvent => auditEvent.OccurredAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(auditEvent => auditEvent.Action).HasMaxLength(150).IsRequired();
            entity.Property(auditEvent => auditEvent.ResourceType).HasMaxLength(100).IsRequired();
            entity.Property(auditEvent => auditEvent.Reason).HasMaxLength(1000);
            entity.Property(auditEvent => auditEvent.BeforeJson).HasColumnType("jsonb");
            entity.Property(auditEvent => auditEvent.AfterJson).HasColumnType("jsonb");
            entity.HasIndex(auditEvent => new { auditEvent.ResourceType, auditEvent.ResourceId });
            entity.HasIndex(auditEvent => auditEvent.SiteId);
            entity.HasIndex(auditEvent => auditEvent.OccurredAtUtc);
        });
    }
}
