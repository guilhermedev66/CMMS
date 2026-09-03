using Cmms.BuildingBlocks.Database;
using Cmms.Modules.PreventiveMaintenance.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cmms.Modules.PreventiveMaintenance.Infrastructure;

public sealed class PreventiveMaintenanceDbContext(DbContextOptions<PreventiveMaintenanceDbContext> options) : DbContext(options)
{
    public DbSet<MaintenancePlan> MaintenancePlans => Set<MaintenancePlan>();

    public DbSet<MaintenancePlanOccurrence> MaintenancePlanOccurrences => Set<MaintenancePlanOccurrence>();

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
        builder.HasDefaultSchema(DatabaseSchemas.PreventiveMaintenance);

        builder.Entity<MaintenancePlan>(entity =>
        {
            entity.ToTable(
                "maintenance_plans",
                table =>
                {
                    table.HasCheckConstraint("ck_maintenance_plans_recurrence_type", "recurrence_type IN ('Fixed', 'Floating')");
                    table.HasCheckConstraint("ck_maintenance_plans_status", "status IN ('Active', 'Paused')");
                    table.HasCheckConstraint("ck_maintenance_plans_interval_days", "interval_days > 0");
                    table.HasCheckConstraint("ck_maintenance_plans_lead_time", "generation_lead_time_days >= 0");
                });
            entity.HasKey(plan => plan.Id);
            entity.HasAlternateKey(plan => new { plan.SiteId, plan.Id });
            entity.Property(plan => plan.Title).HasMaxLength(200).IsRequired();
            entity.Property(plan => plan.Description).HasMaxLength(2000);
            entity.Property(plan => plan.RecurrenceType).HasConversion<string>().HasMaxLength(10);
            entity.Property(plan => plan.Status).HasConversion<string>().HasMaxLength(10);
            entity.Property(plan => plan.NextDueAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(plan => plan.CreatedAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(plan => plan.RowVersion).IsConcurrencyToken().HasDefaultValue(1L);
            entity.HasIndex(plan => plan.SiteId);
            entity.HasIndex(plan => new { plan.Status, plan.NextDueAtUtc });

            // No FK to assets.assets / work_management.work_orders — same schema-per-module
            // rationale as MaintenanceRequestsDbContext/WorkManagementDbContext (see those
            // contexts' comments): the endpoint validates same-site membership before insert, and
            // MaintenancePlanOccurrence below is the (unenforced-by-FK, enforced-by-uniqueness)
            // link to the generated Work Order.
        });

        builder.Entity<MaintenancePlanOccurrence>(entity =>
        {
            entity.ToTable("maintenance_plan_occurrences");
            entity.HasKey(occurrence => occurrence.Id);
            entity.Property(occurrence => occurrence.ScheduledForUtc).HasColumnType("timestamp with time zone");
            entity.Property(occurrence => occurrence.GeneratedAtUtc).HasColumnType("timestamp with time zone");
            entity.HasIndex(occurrence => occurrence.SiteId);
            // docs/01: unique (plan_id, scheduled_for) — "the final safety net even if the lock
            // protocol is ever bypassed".
            entity.HasIndex(occurrence => new { occurrence.PlanId, occurrence.ScheduledForUtc }).IsUnique();
            entity.HasIndex(occurrence => occurrence.WorkOrderId).IsUnique();
            entity.HasOne<MaintenancePlan>()
                .WithMany()
                .HasForeignKey(occurrence => occurrence.PlanId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private void IncrementRowVersions()
    {
        foreach (var entry in ChangeTracker.Entries()
                     .Where(entry => entry.State == EntityState.Modified && entry.Entity is MaintenancePlan))
        {
            var property = entry.Property(nameof(MaintenancePlan.RowVersion));
            property.CurrentValue = (long)property.OriginalValue! + 1;
        }
    }
}
