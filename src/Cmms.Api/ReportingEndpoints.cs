using Cmms.Modules.IdentityAccess.Application;
using Cmms.Modules.IdentityAccess.Domain;
using Cmms.Modules.PreventiveMaintenance.Domain;
using Cmms.Modules.PreventiveMaintenance.Infrastructure;
using Cmms.Modules.WorkManagement.Domain;
using Cmms.Modules.WorkManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Cmms.Api;

/// <summary>
/// M5 — Reporting &amp; Operations. Every formula below is cited to
/// docs/01-domain-and-workflows.md § "KPI formulas" (itself sourced from SMRP Best Practice Guide /
/// ISO 14224 / EN 13306, not invented) — see each metric's comment for the exact source line and
/// the scope cut where this codebase's actual schema doesn't carry a field the textbook formula
/// wants (no staffing/crew table, no per-WO estimated-labor-hours field, no asset replacement-value
/// field, no contractor-invoice ledger). Every scope cut is named at the point it's made, not
/// silently approximated. No pre-computed KPI is ever persisted (docs/01: "never persist
/// pre-computed averages — compute on demand") — this endpoint recomputes from the same raw
/// transactional rows every call.
///
/// Gated behind <see cref="PermissionCatalog.WorkOrdersReadAll"/> (Planner/Admin only, same as the
/// existing catalog — Technician's grant is <c>OwnAssignment</c>-scoped, not site-wide, so a
/// Technician correctly cannot see the operational dashboard) — reusing that permission rather than
/// minting a new one, since site-wide Work Order visibility is exactly the predicate a reporting
/// view needs. Cost figures are additionally masked to <c>null</c> for a caller without <see
/// cref="PermissionCatalog.CostsView"/>, same field-level masking pattern used for part-usage costs
/// in src/Cmms.Api/WorkOrderExecutionEndpoints.cs.
/// </summary>
internal static class ReportingEndpoints
{
    public static IEndpointRouteBuilder MapReportingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/reports/kpis", GetKpisAsync).WithTags("Reporting").RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> GetKpisAsync(
        Guid siteId,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        Guid? assetId,
        HttpContext httpContext,
        WorkManagementDbContext workOrdersDb,
        PreventiveMaintenanceDbContext plansDb,
        IPermissionEvaluator permissions,
        CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(httpContext.User, PermissionCatalog.WorkOrdersReadAll, siteId, cancellationToken))
        {
            return Results.NotFound();
        }

        var to = toUtc ?? DateTimeOffset.UtcNow;
        var from = fromUtc ?? to.AddDays(-30);
        if (from >= to)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["fromUtc"] = ["fromUtc must be strictly before toUtc."]
            });
        }

        var canViewCosts = await permissions.HasPermissionAsync(httpContext.User, PermissionCatalog.CostsView, siteId, cancellationToken);

        // ---------- Completed/Closed Work Orders whose completion falls in the window ----------
        // "Completed" here means reached at least Completed (Closed included) — a Cancelled order
        // never contributes to any of these figures; an order Reopened after this window's Complete
        // and completed again *within* the window is counted once, at its latest completion, per
        // docs/01: "KPI aggregation uses the latest closed cycle's completion timestamp."
        IQueryable<WorkOrder> completedQuery = workOrdersDb.WorkOrders.AsNoTracking()
            .Where(w => w.SiteId == siteId &&
                        w.Status != WorkOrderStatus.Cancelled &&
                        w.CompletedAtUtc != null &&
                        w.CompletedAtUtc >= from && w.CompletedAtUtc < to);
        if (assetId is not null)
        {
            completedQuery = completedQuery.Where(w => w.AssetId == assetId);
        }

        var completed = await completedQuery
            .Select(w => new { w.Id, w.AssetId, w.WrenchStartAtUtc, w.CompletedAtUtc, w.SourceRequestId })
            .ToListAsync(cancellationToken);

        var completedIds = completed.Select(w => w.Id).ToHashSet();

        // A Work Order is "Preventive" iff a MaintenancePlanOccurrence points at it (docs/01's
        // preventive flow — generation always creates exactly this link); everything else is
        // "Corrective". This is a derived classification, not a stored flag, so it can never drift
        // from the actual generation record.
        var preventiveWorkOrderIds = completedIds.Count == 0
            ? []
            : await plansDb.MaintenancePlanOccurrences.AsNoTracking()
                .Where(o => completedIds.Contains(o.WorkOrderId))
                .Select(o => o.WorkOrderId)
                .ToListAsync(cancellationToken);
        var preventiveIdSet = preventiveWorkOrderIds.ToHashSet();

        var preventiveCount = completed.Count(w => preventiveIdSet.Contains(w.Id));
        var correctiveCount = completed.Count - preventiveCount;

        // ---------- Planned Maintenance Percentage ----------
        // docs/01: "PMP = Labor Hours on Planned PM / Total Labor Hours (PM + Reactive) x 100%."
        // SCOPE CUT: this schema has no per-Work-Order estimated/actual labor-hours field or labor
        // ledger (docs/01's own M4 scope cut — see WorkOrder.MarkCompleted's doc comment). The one
        // labor-adjacent fact every completed Work Order actually carries is its wrench-time span
        // (wrench_start_at_utc -> completed_at_utc), so that duration is used as the labor-hours
        // proxy here — consistent with, not a new deviation from, that already-documented M4 cut.
        double? plannedMaintenancePercentage = null;
        var withWrenchTime = completed.Where(w => w.WrenchStartAtUtc is not null).ToList();
        var totalWrenchHours = withWrenchTime.Sum(w => (w.CompletedAtUtc!.Value - w.WrenchStartAtUtc!.Value).TotalHours);
        if (totalWrenchHours > 0)
        {
            var plannedWrenchHours = withWrenchTime
                .Where(w => preventiveIdSet.Contains(w.Id))
                .Sum(w => (w.CompletedAtUtc!.Value - w.WrenchStartAtUtc!.Value).TotalHours);
            plannedMaintenancePercentage = plannedWrenchHours / totalWrenchHours * 100.0;
        }

        // ---------- Failure population (MTBF/MTTR/MDT/Availability) — per-asset only ----------
        // These four are inherently per-equipment metrics (docs/01 cites MTBF as "operating time
        // between failures" for *an* asset) — averaging them across a site's heterogeneous asset mix
        // without a weighting model this system doesn't have would not be mathematically defensible,
        // so they are reported only when assetId is supplied, and explicitly null otherwise (never
        // silently averaged or defaulted).
        double? mtbfHours = null;
        double? mttrHours = null;
        double? mdtHours = null;
        double? operationalAvailability = null;
        double? inherentAvailability = null;

        if (assetId is not null)
        {
            var closedFullStopIntervals = await workOrdersDb.DowntimeIntervals.AsNoTracking()
                .Where(i => i.SiteId == siteId && i.AssetId == assetId.Value &&
                            i.Classification == DowntimeClassification.FullStop &&
                            i.StartedAtUtc < to && (i.EndedAtUtc == null || i.EndedAtUtc > from))
                .Select(i => new { i.WorkOrderId, i.StartedAtUtc, i.EndedAtUtc })
                .ToListAsync(cancellationToken);

            // Clip every interval to the window — a breakdown that started before `from` or is
            // still open past `to` only contributes the portion that actually falls inside the
            // reporting window (an explicit choice for the "timezone/boundary" correctness the M5
            // DoD asks to test, not an oversight).
            var totalDowntimeHours = closedFullStopIntervals.Sum(i =>
            {
                var effectiveStart = i.StartedAtUtc < from ? from : i.StartedAtUtc;
                var effectiveEnd = i.EndedAtUtc is null || i.EndedAtUtc > to ? to : i.EndedAtUtc.Value;
                return effectiveEnd > effectiveStart ? (effectiveEnd - effectiveStart).TotalHours : 0.0;
            });

            var totalAvailableHours = (to - from).TotalHours;

            operationalAvailability = totalAvailableHours > 0
                ? (totalAvailableHours - totalDowntimeHours) / totalAvailableHours
                : null;

            mdtHours = closedFullStopIntervals.Count > 0 ? closedFullStopIntervals.Average(i =>
            {
                var effectiveStart = i.StartedAtUtc < from ? from : i.StartedAtUtc;
                var effectiveEnd = i.EndedAtUtc is null || i.EndedAtUtc > to ? to : i.EndedAtUtc.Value;
                return (effectiveEnd - effectiveStart).TotalHours;
            }) : null;

            // "Failure Work Order" = a Corrective Work Order (in this window's completed set) that
            // has at least one FullStop downtime interval linked to it — docs/01 ties a machine-down
            // corrective order to exactly this fact ("cannot close without started_at/ended_at and a
            // cause code recorded"). Preventive Work Orders are excluded from the failure count even
            // if one happens to carry downtime (a planned shutdown is not a failure).
            var failureWorkOrderIds = closedFullStopIntervals.Select(i => i.WorkOrderId).ToHashSet();
            var failureWorkOrders = completed
                .Where(w => w.AssetId == assetId.Value && !preventiveIdSet.Contains(w.Id) && failureWorkOrderIds.Contains(w.Id))
                .ToList();

            if (failureWorkOrders.Count > 0)
            {
                // MTBF = (Total Available Time - Total Downtime) / Count of Failure Work Orders.
                mtbfHours = (totalAvailableHours - totalDowntimeHours) / failureWorkOrders.Count;

                var repairDurations = failureWorkOrders
                    .Where(w => w.WrenchStartAtUtc is not null)
                    .Select(w => (w.CompletedAtUtc!.Value - w.WrenchStartAtUtc!.Value).TotalHours)
                    .ToList();
                mttrHours = repairDurations.Count > 0 ? repairDurations.Average() : null;

                inherentAvailability = mttrHours is not null ? mtbfHours / (mtbfHours + mttrHours) : null;
            }
            // else: mtbfHours/mttrHours/inherentAvailability stay null — "undefined... never
            // silently rendered as 0 or infinity" (docs/01), a zero-failure period is good news, not
            // a metric of 0 or an unbounded one.
        }

        // ---------- Parts cost (masked for a caller without costs.view) ----------
        IQueryable<PartUsage> partUsageQuery = workOrdersDb.PartUsages.AsNoTracking()
            .Where(p => p.SiteId == siteId && p.CreatedAtUtc >= from && p.CreatedAtUtc < to);
        if (assetId is not null)
        {
            var assetWorkOrderIds = await workOrdersDb.WorkOrders.AsNoTracking()
                .Where(w => w.SiteId == siteId && w.AssetId == assetId)
                .Select(w => w.Id)
                .ToListAsync(cancellationToken);
            partUsageQuery = partUsageQuery.Where(p => assetWorkOrderIds.Contains(p.WorkOrderId));
        }

        decimal? totalPartsCost = null;
        if (canViewCosts)
        {
            var usages = await partUsageQuery.Select(p => new { p.Quantity, p.UnitCost }).ToListAsync(cancellationToken);
            totalPartsCost = usages.Sum(u => u.Quantity * u.UnitCost);
        }

        // ---------- Live snapshots (not period-scoped): backlog and overdue preventive ----------
        // SCOPE CUT: docs/01's Backlog formula needs crew/staffing hours ("Technicians x Shift
        // Hours/Week - PTO") and an estimated-labor-hours field on the Work Order — neither exists
        // in this schema (no staffing entity was ever built). Reporting a raw open-order count
        // instead of a fabricated crew-weeks number is the honest choice; a real crew-weeks figure
        // is deferred until a staffing model exists, named here rather than approximated silently.
        IQueryable<WorkOrder> backlogQuery = workOrdersDb.WorkOrders.AsNoTracking().Where(w =>
            w.SiteId == siteId &&
            (w.Status == WorkOrderStatus.Open || w.Status == WorkOrderStatus.Scheduled || w.Status == WorkOrderStatus.InProgress));
        if (assetId is not null)
        {
            backlogQuery = backlogQuery.Where(w => w.AssetId == assetId);
        }
        var openBacklogCount = await backlogQuery.CountAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        IQueryable<MaintenancePlan> overdueQuery = plansDb.MaintenancePlans.AsNoTracking()
            .Where(p => p.SiteId == siteId && p.Status == MaintenancePlanStatus.Active && p.NextDueAtUtc < now);
        if (assetId is not null)
        {
            overdueQuery = overdueQuery.Where(p => p.AssetId == assetId);
        }
        var overduePreventiveCount = await overdueQuery.CountAsync(cancellationToken);

        return Results.Ok(new KpiReportResponse(
            SiteId: siteId,
            AssetId: assetId,
            FromUtc: from,
            ToUtc: to,
            MtbfHours: mtbfHours,
            MttrHours: mttrHours,
            MdtHours: mdtHours,
            OperationalAvailability: operationalAvailability,
            InherentAvailability: inherentAvailability,
            PlannedMaintenancePercentage: plannedMaintenancePercentage,
            PreventiveWorkOrderCount: preventiveCount,
            CorrectiveWorkOrderCount: correctiveCount,
            TotalPartsCost: totalPartsCost,
            CostsMasked: !canViewCosts,
            OpenBacklogCount: openBacklogCount,
            OverduePreventivePlanCount: overduePreventiveCount));
    }
}

/// <summary>
/// A null numeric field means the metric is mathematically undefined for this window/scope (e.g.
/// zero failures for MTBF, or MTBF/MTTR themselves being per-asset-only and no assetId was
/// supplied) — never a silently substituted 0 or infinity, per docs/01's explicit requirement.
/// </summary>
internal sealed record KpiReportResponse(
    Guid SiteId,
    Guid? AssetId,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    double? MtbfHours,
    double? MttrHours,
    double? MdtHours,
    double? OperationalAvailability,
    double? InherentAvailability,
    double? PlannedMaintenancePercentage,
    int PreventiveWorkOrderCount,
    int CorrectiveWorkOrderCount,
    decimal? TotalPartsCost,
    bool CostsMasked,
    int OpenBacklogCount,
    int OverduePreventivePlanCount);
