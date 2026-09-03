using Cmms.BuildingBlocks.Database;
using Cmms.Modules.WorkManagement.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cmms.Modules.WorkManagement.Infrastructure;

public sealed class WorkManagementDbContext(DbContextOptions<WorkManagementDbContext> options) : DbContext(options)
{
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();

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
