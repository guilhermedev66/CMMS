using Cmms.BuildingBlocks.Database;
using Cmms.Modules.Audit.Application;
using Cmms.Modules.Audit.Infrastructure;
using Cmms.Modules.PreventiveMaintenance.Domain;
using Cmms.Modules.PreventiveMaintenance.Infrastructure;
using Cmms.Modules.WorkManagement.Domain;
using Cmms.Modules.WorkManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Cmms.Api;

/// <summary>
/// Preventive Work Order generation, per docs/01-domain-and-workflows.md § "Preventive maintenance
/// flow" and its "Resolves QA finding B-04(2)" note, and docs/02-security-and-invariants.md's
/// concurrency table row "Two scheduler ticks/instances". Split into two phases exactly as those
/// docs describe:
///
/// <list type="number">
/// <item>Batch-claim: a short, separately-committed <c>SELECT ... FOR UPDATE SKIP LOCKED</c> that
/// only decides which plan ids *this* call will attempt this sweep — "about work distribution, not
/// correctness" (docs/02). Releasing the lock immediately means a genuinely concurrent second
/// caller (another instance, or this same test calling it twice in parallel) can still pick a
/// *different* candidate from the same due set; the actual duplicate-prevention guarantee below
/// doesn't depend on this phase serializing anything.</item>
/// <item>Per-plan generation: a fresh transaction re-locks the one plan row with a blocking
/// <c>SELECT ... FOR UPDATE</c>, re-validates it is still <c>Active</c> and still has no
/// <see cref="MaintenancePlan.ActiveOccurrenceId"/> (a concurrent caller's phase 2 for the *same*
/// plan simply blocks here, then sees the now-set pointer and no-ops), and only then inserts the
/// occurrence + creates the Work Order + advances the plan, all in that one transaction. The
/// occurrence's unique <c>(plan_id, scheduled_for)</c> index is the final safety net "even if the
/// lock protocol is ever bypassed" (docs/01).</item>
/// </list>
///
/// This type is called directly by integration tests (including concurrently, to simulate "two
/// scheduler ticks" or "two instances") rather than only through the timer-driven
/// <see cref="MaintenancePlanGenerationService"/>, so the idempotency guarantee is provable without
/// waiting on a real timer.
/// </summary>
public interface IMaintenancePlanGenerationRunner
{
    Task<int> RunSweepAsync(CancellationToken cancellationToken = default);
}

public sealed class MaintenancePlanGenerationRunner(IConfiguration configuration, IAuditEventWriter auditWriter) : IMaintenancePlanGenerationRunner
{
    private const int BatchSize = 25;

    public async Task<int> RunSweepAsync(CancellationToken cancellationToken = default)
    {
        var connectionString = configuration.GetConnectionString("Cmms")
            ?? throw new InvalidOperationException("Connection string 'Cmms' is not configured.");

        var candidatePlanIds = await ClaimCandidatesAsync(connectionString, cancellationToken);

        var generatedCount = 0;
        foreach (var planId in candidatePlanIds)
        {
            if (await TryGenerateForPlanAsync(connectionString, planId, cancellationToken))
            {
                generatedCount++;
            }
        }

        return generatedCount;
    }

    /// <summary>Phase 1 — see this type's doc comment.</summary>
    private static async Task<IReadOnlyList<Guid>> ClaimCandidatesAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var transactionScope = await SharedTransactionScope.BeginAsync(connectionString, cancellationToken);
        await using var plansDb = transactionScope.CreateContext<PreventiveMaintenanceDbContext>(options => new PreventiveMaintenanceDbContext(options));

        var now = DateTimeOffset.UtcNow;
        var candidateIds = await plansDb.MaintenancePlans
            .FromSqlInterpolated(
                $"""
                 SELECT * FROM preventive_maintenance.maintenance_plans
                 WHERE status = 'Active' AND (next_due_at_utc - make_interval(days => generation_lead_time_days)) <= {now}
                 ORDER BY next_due_at_utc
                 LIMIT {BatchSize}
                 FOR UPDATE SKIP LOCKED
                 """)
            .Select(plan => plan.Id)
            .ToListAsync(cancellationToken);

        // Commit (rather than roll back) so this short transaction's row locks release right away —
        // per this type's doc comment, phase 1 is only about picking candidates, not holding a lock
        // across into phase 2.
        await transactionScope.CommitAsync(cancellationToken);
        return candidateIds;
    }

    /// <summary>Phase 2 — see this type's doc comment. Returns true if this call generated a new occurrence.</summary>
    private async Task<bool> TryGenerateForPlanAsync(string connectionString, Guid planId, CancellationToken cancellationToken)
    {
        await using var transactionScope = await SharedTransactionScope.BeginAsync(connectionString, cancellationToken);
        await using var plansDb = transactionScope.CreateContext<PreventiveMaintenanceDbContext>(options => new PreventiveMaintenanceDbContext(options));
        await using var workOrdersDb = transactionScope.CreateContext<WorkManagementDbContext>(options => new WorkManagementDbContext(options));
        await using var auditDb = transactionScope.CreateContext<AuditDbContext>(options => new AuditDbContext(options));

        var plan = await plansDb.MaintenancePlans
            .FromSqlInterpolated($"SELECT * FROM preventive_maintenance.maintenance_plans WHERE id = {planId} FOR UPDATE")
            .FirstOrDefaultAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        if (plan is null
            || plan.Status != MaintenancePlanStatus.Active
            || plan.ActiveOccurrenceId is not null
            || plan.NextDueAtUtc.AddDays(-plan.GenerationLeadTimeDays) > now)
        {
            // Re-validated under the lock and no longer eligible — a pause/edit or a concurrent
            // phase-2 caller for this same plan already landed since the phase-1 read. Nothing to
            // do; SuppressIfOpen (ActiveOccurrenceId not null) is the expected common case here.
            await transactionScope.CommitAsync(cancellationToken);
            return false;
        }

        // Starts Draft, same as a direct create or a Request conversion — a Planner still
        // explicitly Publishes it (consistent across all three Work Order creation paths in this
        // codebase; docs/01's transition table doesn't special-case Convert or PM generation into
        // skipping that review step).
        var workOrder = new WorkOrder(
            plan.SiteId,
            plan.Title,
            plan.Description,
            plan.AssetId,
            locationId: null,
            createdByUserId: plan.CreatedByUserId,
            priority: WorkOrderPriority.P3);
        workOrdersDb.WorkOrders.Add(workOrder);
        await workOrdersDb.SaveChangesAsync(cancellationToken);

        var occurrence = new MaintenancePlanOccurrence(plan.Id, plan.SiteId, plan.NextDueAtUtc, workOrder.Id);
        plansDb.MaintenancePlanOccurrences.Add(occurrence);

        plan.RecordGeneration(occurrence.Id);
        await plansDb.SaveChangesAsync(cancellationToken);

        await auditWriter.WriteAsync(
            auditDb,
            new AuditEventEntry(
                ActorUserId: null,
                Action: "maintenanceplan.occurrence.generated",
                ResourceType: "MaintenancePlan",
                ResourceId: plan.Id,
                SiteId: plan.SiteId,
                CorrelationId: workOrder.Id,
                Reason: null,
                BeforeJson: null,
                AfterJson: $$"""{"occurrenceId":"{{occurrence.Id}}","workOrderId":"{{workOrder.Id}}","scheduledForUtc":"{{occurrence.ScheduledForUtc:O}}"}"""),
            cancellationToken);

        await transactionScope.CommitAsync(cancellationToken);
        return true;
    }
}
