using Cmms.BuildingBlocks.Database;
using Cmms.Modules.MaintenanceRequests.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cmms.Modules.MaintenanceRequests.Infrastructure;

public sealed class MaintenanceRequestsDbContext(DbContextOptions<MaintenanceRequestsDbContext> options) : DbContext(options)
{
    public DbSet<MaintenanceRequest> Requests => Set<MaintenanceRequest>();

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
        builder.HasDefaultSchema(DatabaseSchemas.MaintenanceRequests);

        builder.Entity<MaintenanceRequest>(entity =>
        {
            entity.ToTable(
                "requests",
                table =>
                {
                    table.HasCheckConstraint("ck_requests_status", "status IN ('New', 'Converted', 'Rejected', 'Cancelled')");
                    table.HasCheckConstraint(
                        "ck_requests_asset_or_location",
                        "asset_id IS NOT NULL OR location_id IS NOT NULL");
                    table.HasCheckConstraint("ck_requests_priority", "priority IN ('P1', 'P2', 'P3', 'P4')");
                });
            entity.HasKey(request => request.Id);
            entity.HasAlternateKey(request => new { request.SiteId, request.Id });
            entity.Property(request => request.Title).HasMaxLength(200).IsRequired();
            entity.Property(request => request.Description).HasMaxLength(2000);
            entity.Property(request => request.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(request => request.Priority).HasConversion<string>().HasMaxLength(2);
            entity.Property(request => request.RejectedReason).HasMaxLength(1000);
            entity.Property(request => request.CreatedAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(request => request.ResolvedAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(request => request.RowVersion).IsConcurrencyToken().HasDefaultValue(1L);

            // No FK to assets.assets/assets.locations here: this module doesn't reference the
            // Assets module's entities (schema-per-module boundary, per
            // docs/03-architecture-decisions.md), and the endpoint already validates same-site
            // membership before insert (same pattern as AssetsEndpoints' ParentAssetId check).
            entity.HasIndex(request => request.SiteId);
            entity.HasIndex(request => request.CreatedByUserId);
            entity.HasIndex(request => request.Status);

            // Unique only when set (docs/01: "converted_work_order_id on the Request ... unique").
            // No cross-schema FK to work_management.work_orders: that would require this module's
            // migration to run after WorkManagement's, while a symmetric FK on the Work Order side
            // (source_request_id -> requests.id) would require the opposite ordering — a circular
            // dependency. Referential correctness is enforced at the application layer (the convert
            // endpoint loads/creates both rows in one transaction); the uniqueness itself (the actual
            // "at most one WO per request" invariant) is still DB-enforced below.
            entity.HasIndex(request => request.ConvertedWorkOrderId)
                .IsUnique()
                .HasFilter("converted_work_order_id IS NOT NULL");
        });
    }

    private void IncrementRowVersions()
    {
        foreach (var entry in ChangeTracker.Entries()
                     .Where(entry => entry.State == EntityState.Modified && entry.Entity is MaintenanceRequest))
        {
            var property = entry.Property(nameof(MaintenanceRequest.RowVersion));
            property.CurrentValue = (long)property.OriginalValue! + 1;
        }
    }
}
