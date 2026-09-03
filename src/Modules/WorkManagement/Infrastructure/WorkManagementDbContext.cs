using Cmms.BuildingBlocks.Database;
using Cmms.Modules.WorkManagement.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cmms.Modules.WorkManagement.Infrastructure;

public sealed class WorkManagementDbContext(DbContextOptions<WorkManagementDbContext> options) : DbContext(options)
{
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();

    public DbSet<ChecklistItem> ChecklistItems => Set<ChecklistItem>();

    public DbSet<DowntimeInterval> DowntimeIntervals => Set<DowntimeInterval>();

    public DbSet<PartUsage> PartUsages => Set<PartUsage>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        IncrementRowVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        IncrementRowVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(DatabaseSchemas.WorkManagement);

        builder.Entity<WorkOrder>(entity =>
        {
            entity.ToTable(
                "work_orders",
                table =>
                {
                    table.HasCheckConstraint(
                        "ck_work_orders_status",
                        "status IN ('Draft', 'Open', 'Scheduled', 'InProgress', 'Completed', 'Closed', 'Cancelled')");
                    table.HasCheckConstraint("ck_work_orders_priority", "priority IN ('P1', 'P2', 'P3', 'P4')");
                });
            entity.HasKey(workOrder => workOrder.Id);
            entity.HasAlternateKey(workOrder => new { workOrder.SiteId, workOrder.Id });
            entity.Property(workOrder => workOrder.Title).HasMaxLength(200).IsRequired();
            entity.Property(workOrder => workOrder.Description).HasMaxLength(2000);
            entity.Property(workOrder => workOrder.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(workOrder => workOrder.Priority).HasConversion<string>().HasMaxLength(2);
            entity.Property(workOrder => workOrder.CancelReason).HasMaxLength(1000);
            entity.Property(workOrder => workOrder.ReopenReason).HasMaxLength(1000);
            entity.Property(workOrder => workOrder.CreatedAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(workOrder => workOrder.AssignedAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(workOrder => workOrder.WrenchStartAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(workOrder => workOrder.CompletedAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(workOrder => workOrder.ClosedAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(workOrder => workOrder.CancelledAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(workOrder => workOrder.RowVersion).IsConcurrencyToken().HasDefaultValue(1L);

            // No FK to assets.assets/assets.locations or maintenance_requests.requests, same
            // schema-per-module rationale as MaintenanceRequestsDbContext (see that context's
            // comment on ConvertedWorkOrderId) — the source_request_id side of that same circular
            // pair, plus asset_id/location_id which this module doesn't own.
            entity.HasIndex(workOrder => workOrder.SiteId);
            entity.HasIndex(workOrder => new { workOrder.SiteId, workOrder.Status });
            entity.HasIndex(workOrder => workOrder.AssigneeId);

            // docs/01: "source_request_id on the Work Order" is unique — at most one Work Order
            // per source Request.
            entity.HasIndex(workOrder => workOrder.SourceRequestId)
                .IsUnique()
                .HasFilter("source_request_id IS NOT NULL");
        });

        builder.Entity<ChecklistItem>(entity =>
        {
            entity.ToTable(
                "checklist_items",
                table =>
                {
                    table.HasCheckConstraint(
                        "ck_checklist_items_item_type",
                        "item_type IN ('Boolean', 'Numeric', 'SingleSelect', 'PhotoRequired', 'Note')");
                });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Label).HasMaxLength(300).IsRequired();
            entity.Property(item => item.ItemType).HasConversion<string>().HasMaxLength(20);
            entity.Property(item => item.NumericUnit).HasMaxLength(30);
            entity.Property(item => item.SingleSelectOptionsCsv).HasMaxLength(1000);
            entity.Property(item => item.SelectedOption).HasMaxLength(200);
            entity.Property(item => item.NoteText).HasMaxLength(2000);
            entity.Property(item => item.NumericValue).HasColumnType("numeric(18,4)");
            entity.Property(item => item.NumericMinValue).HasColumnType("numeric(18,4)");
            entity.Property(item => item.NumericMaxValue).HasColumnType("numeric(18,4)");
            entity.Property(item => item.CreatedAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(item => item.ResolvedAtUtc).HasColumnType("timestamp with time zone");
            entity.HasIndex(item => new { item.WorkOrderId, item.ExecutionCycle });
            entity.HasOne<WorkOrder>()
                .WithMany()
                .HasForeignKey(item => item.WorkOrderId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<DowntimeInterval>(entity =>
        {
            entity.ToTable(
                "downtime_intervals",
                table =>
                {
                    table.HasCheckConstraint(
                        "ck_downtime_intervals_classification",
                        "classification IN ('FullStop', 'PartialDerating')");
                    table.HasCheckConstraint(
                        "ck_downtime_intervals_cause_category",
                        "cause_category IS NULL OR cause_category IN ('Mechanical', 'Electrical', 'Hydraulic', 'Pneumatic', 'Instrumentation', 'Operational')");
                    table.HasCheckConstraint(
                        "ck_downtime_intervals_ended_after_started",
                        "ended_at_utc IS NULL OR ended_at_utc >= started_at_utc");
                });
            entity.HasKey(interval => interval.Id);
            entity.Property(interval => interval.Classification).HasConversion<string>().HasMaxLength(20);
            entity.Property(interval => interval.CauseCategory).HasConversion<string>().HasMaxLength(20);
            entity.Property(interval => interval.CauseMechanism).HasMaxLength(200);
            entity.Property(interval => interval.StartedAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(interval => interval.EndedAtUtc).HasColumnType("timestamp with time zone");
            entity.HasIndex(interval => new { interval.WorkOrderId, interval.ExecutionCycle });
            entity.HasIndex(interval => interval.AssetId);
            entity.HasOne<WorkOrder>()
                .WithMany()
                .HasForeignKey(interval => interval.WorkOrderId)
                .OnDelete(DeleteBehavior.Restrict);
            // The FullStop no-overlap exclusion constraint is added via raw SQL in the migration
            // (EF Core's fluent API has no first-class support for PostgreSQL EXCLUDE constraints).
        });

        builder.Entity<PartUsage>(entity =>
        {
            entity.ToTable("part_usages");
            entity.HasKey(usage => usage.Id);
            entity.Property(usage => usage.PartName).HasMaxLength(200).IsRequired();
            entity.Property(usage => usage.PartCode).HasMaxLength(100);
            entity.Property(usage => usage.Quantity).HasColumnType("numeric(18,4)");
            entity.Property(usage => usage.UnitCost).HasColumnType("numeric(18,4)");
            entity.Property(usage => usage.Currency).HasMaxLength(3).IsRequired();
            entity.Property(usage => usage.IdempotencyKey).HasMaxLength(100);
            entity.Property(usage => usage.CreatedAtUtc).HasColumnType("timestamp with time zone");
            entity.HasIndex(usage => new { usage.WorkOrderId, usage.ExecutionCycle });
            // docs/01: "a client-supplied idempotency key deduplicates a retried insert".
            entity.HasIndex(usage => new { usage.WorkOrderId, usage.IdempotencyKey })
                .IsUnique()
                .HasFilter("idempotency_key IS NOT NULL");
            entity.HasOne<WorkOrder>()
                .WithMany()
                .HasForeignKey(usage => usage.WorkOrderId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private void IncrementRowVersions()
    {
        foreach (var entry in ChangeTracker.Entries()
                     .Where(entry => entry.State == EntityState.Modified && entry.Entity is WorkOrder))
        {
            var property = entry.Property(nameof(WorkOrder.RowVersion));
            property.CurrentValue = (long)property.OriginalValue! + 1;
        }
    }
}
