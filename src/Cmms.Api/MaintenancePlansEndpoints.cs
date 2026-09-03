using System.Security.Claims;
using Cmms.Modules.Assets.Infrastructure;
using Cmms.Modules.IdentityAccess.Application;
using Cmms.Modules.IdentityAccess.Domain;
using Cmms.Modules.PreventiveMaintenance.Domain;
using Cmms.Modules.PreventiveMaintenance.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace Cmms.Api;

/// <summary>
/// Maintenance Plan CRUD (create/list/get/pause/resume), per docs/02-security-and-invariants.md's
/// atomic permission table (<c>plans.*</c> rows). The actual occurrence-generation job lives in
/// <c>Cmms.Modules.PreventiveMaintenance.Application.MaintenancePlanGenerationRunner</c> — this
/// file is only the management surface a Planner/Admin uses to define and pause/resume a plan.
/// </summary>
internal static class MaintenancePlansEndpoints
{
    public static IEndpointRouteBuilder MapMaintenancePlansEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var plans = endpoints.MapGroup("/maintenance-plans").WithTags("MaintenancePlans").RequireAuthorization();
        plans.MapGet("", ListPlansAsync);
        plans.MapGet("/{id:guid}", GetPlanAsync);
        plans.MapPost("", CreatePlanAsync);
        plans.MapPost("/{id:guid}/pause", PauseAsync);
        plans.MapPost("/{id:guid}/resume", ResumeAsync);

        return endpoints;
    }

    private static async Task<IResult> ListPlansAsync(
        Guid? siteId,
        ClaimsPrincipal user,
        PreventiveMaintenanceDbContext plansDb,
        IPermissionEvaluator permissions,
        CancellationToken cancellationToken)
    {
        var scope = await permissions.GetSiteScopeAsync(user, PermissionCatalog.PlansRead, cancellationToken);

        IQueryable<MaintenancePlan> query = plansDb.MaintenancePlans.AsNoTracking();
        if (!scope.AllSites)
        {
            if (scope.SiteIds.Count == 0)
            {
                return Results.Ok(Array.Empty<MaintenancePlanResponse>());
            }

            query = query.Where(plan => scope.SiteIds.Contains(plan.SiteId));
        }

        if (siteId is not null)
        {
            if (!scope.Includes(siteId.Value))
            {
                return Results.Ok(Array.Empty<MaintenancePlanResponse>());
            }

            query = query.Where(plan => plan.SiteId == siteId.Value);
        }

        var list = await query.OrderBy(plan => plan.NextDueAtUtc).ToListAsync(cancellationToken);
        return Results.Ok(list.Select(MaintenancePlanResponse.From));
    }

    private static async Task<IResult> GetPlanAsync(
        Guid id,
        ClaimsPrincipal user,
        PreventiveMaintenanceDbContext plansDb,
        IPermissionEvaluator permissions,
        CancellationToken cancellationToken)
    {
        var plan = await plansDb.MaintenancePlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (plan is null)
        {
            return Results.NotFound();
        }

        if (!await permissions.HasPermissionAsync(user, PermissionCatalog.PlansRead, plan.SiteId, cancellationToken))
        {
            return Results.NotFound();
        }

        return Results.Ok(MaintenancePlanResponse.From(plan));
    }

    private static async Task<IResult> CreatePlanAsync(
        CreatePlanRequest request,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        PreventiveMaintenanceDbContext plansDb,
        AssetsDbContext assetsDb,
        IPermissionEvaluator permissions,
        CancellationToken cancellationToken)
    {
        if (!await AntiforgeryHelpers.HasValidAntiforgeryTokenAsync(httpContext, antiforgery))
        {
            return Results.BadRequest(new { error = "Invalid anti-forgery token." });
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["title"] = ["Title is required."]
            });
        }

        if (request.IntervalDays <= 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["intervalDays"] = ["Interval must be a positive number of days."]
            });
        }

        if (!await permissions.HasPermissionAsync(httpContext.User, PermissionCatalog.PlansManage, request.SiteId, cancellationToken))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Not permitted to manage plans at this site.");
        }

        var assetInSameSite = await assetsDb.Assets
            .AnyAsync(asset => asset.Id == request.AssetId && asset.SiteId == request.SiteId, cancellationToken);
        if (!assetInSameSite)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["assetId"] = ["Asset must belong to the same site."]
            });
        }

        var actorUserId = permissions.GetUserId(httpContext.User)
            ?? throw new InvalidOperationException("Authenticated request is missing a user id claim.");

        var plan = new MaintenancePlan(
            request.SiteId,
            request.AssetId,
            request.Title,
            request.Description,
            request.RecurrenceType,
            request.IntervalDays,
            request.GenerationLeadTimeDays,
            request.FirstDueAtUtc,
            actorUserId);

        plansDb.MaintenancePlans.Add(plan);
        await plansDb.SaveChangesAsync(cancellationToken);

        return Results.Created($"/maintenance-plans/{plan.Id}", MaintenancePlanResponse.From(plan));
    }

    private static Task<IResult> PauseAsync(
        Guid id,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        PreventiveMaintenanceDbContext plansDb,
        IPermissionEvaluator permissions,
        CancellationToken cancellationToken) =>
        SetStatusAsync(id, httpContext, antiforgery, plansDb, permissions, plan => plan.Pause(), cancellationToken);

    private static Task<IResult> ResumeAsync(
        Guid id,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        PreventiveMaintenanceDbContext plansDb,
        IPermissionEvaluator permissions,
        CancellationToken cancellationToken) =>
        SetStatusAsync(id, httpContext, antiforgery, plansDb, permissions, plan => plan.Resume(), cancellationToken);

    /// <summary>
    /// Pause/Resume both just flip <see cref="MaintenancePlanStatus"/> under the plan row's lock —
    /// no cross-module coordination needed (unlike the generation job), since pausing doesn't touch
    /// any already-active occurrence. docs/01's B-04(2) note calls out that a pause/edit committed
    /// after the generation job's phase-1 batch-claim but before its phase-2 lock is safely visible
    /// there — this endpoint's own row lock is what makes that true.
    /// </summary>
    private static async Task<IResult> SetStatusAsync(
        Guid id,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        PreventiveMaintenanceDbContext plansDb,
        IPermissionEvaluator permissions,
        Action<MaintenancePlan> mutate,
        CancellationToken cancellationToken)
    {
        if (!await AntiforgeryHelpers.HasValidAntiforgeryTokenAsync(httpContext, antiforgery))
        {
            return Results.BadRequest(new { error = "Invalid anti-forgery token." });
        }

        var plan = await plansDb.MaintenancePlans
            .FromSqlInterpolated($"SELECT * FROM preventive_maintenance.maintenance_plans WHERE id = {id} FOR UPDATE")
            .FirstOrDefaultAsync(cancellationToken);
        if (plan is null)
        {
            return Results.NotFound();
        }

        if (!await permissions.HasPermissionAsync(httpContext.User, PermissionCatalog.PlansManage, plan.SiteId, cancellationToken))
        {
            return Results.NotFound();
        }

        try
        {
            mutate(plan);
        }
        catch (InvalidMaintenancePlanOperationException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }

        await plansDb.SaveChangesAsync(cancellationToken);
        return Results.Ok(MaintenancePlanResponse.From(plan));
    }
}

// ---------- Requests ----------

internal sealed record CreatePlanRequest(
    Guid SiteId,
    Guid AssetId,
    string Title,
    string? Description,
    RecurrenceType RecurrenceType,
    int IntervalDays,
    int GenerationLeadTimeDays,
    DateTimeOffset FirstDueAtUtc);

// ---------- Responses ----------

internal sealed record MaintenancePlanResponse(
    Guid Id,
    Guid SiteId,
    Guid AssetId,
    string Title,
    string? Description,
    RecurrenceType RecurrenceType,
    int IntervalDays,
    int GenerationLeadTimeDays,
    MaintenancePlanStatus Status,
    DateTimeOffset NextDueAtUtc,
    Guid? ActiveOccurrenceId,
    DateTimeOffset CreatedAtUtc,
    long RowVersion)
{
    public static MaintenancePlanResponse From(MaintenancePlan plan) => new(
        plan.Id,
        plan.SiteId,
        plan.AssetId,
        plan.Title,
        plan.Description,
        plan.RecurrenceType,
        plan.IntervalDays,
        plan.GenerationLeadTimeDays,
        plan.Status,
        plan.NextDueAtUtc,
        plan.ActiveOccurrenceId,
        plan.CreatedAtUtc,
        plan.RowVersion);
}
