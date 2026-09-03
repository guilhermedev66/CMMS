using System.Security.Claims;
using System.Text.Json;
using Cmms.BuildingBlocks.Database;
using Cmms.Modules.Assets.Infrastructure;
using Cmms.Modules.Audit.Application;
using Cmms.Modules.Audit.Infrastructure;
using Cmms.Modules.IdentityAccess.Application;
using Cmms.Modules.IdentityAccess.Domain;
using Cmms.Modules.MaintenanceRequests.Domain;
using Cmms.Modules.MaintenanceRequests.Infrastructure;
using Cmms.Modules.WorkManagement.Domain;
using Cmms.Modules.WorkManagement.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace Cmms.Api;

/// <summary>
/// Maintenance Request intake + resolution, per docs/01-domain-and-workflows.md § "Corrective
/// maintenance flow" and docs/02-security-and-invariants.md's atomic permission table
/// (<c>requests.*</c> rows). Convert/Reject/Cancel are each a single conditional
/// <c>UPDATE ... WHERE status = 'New'</c> executed inside the same transaction as the audit write
/// (and, for Convert, the created Work Order) — never a read-then-write domain method, per
/// <see cref="MaintenanceRequest"/>'s doc comment and docs/01's "Resolves QA finding B-04(1)" note.
/// </summary>
internal static class MaintenanceRequestsEndpoints
{
    public static IEndpointRouteBuilder MapMaintenanceRequestsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var requests = endpoints.MapGroup("/requests").WithTags("Requests").RequireAuthorization();
        requests.MapGet("", ListRequestsAsync);
        requests.MapGet("/{id:guid}", GetRequestAsync);
        requests.MapPost("", CreateRequestAsync);
        requests.MapPost("/{id:guid}/convert", ConvertRequestAsync);
        requests.MapPost("/{id:guid}/reject", RejectRequestAsync);
        requests.MapPost("/{id:guid}/cancel", CancelRequestAsync);

        return endpoints;
    }

    private static async Task<IResult> ListRequestsAsync(
        Guid? siteId,
        ClaimsPrincipal user,
        MaintenanceRequestsDbContext requestsDb,
        IPermissionEvaluator permissions,
        CancellationToken cancellationToken)
    {
        var userId = permissions.GetUserId(user);
        var readAll = await permissions.GetSiteScopeAsync(user, PermissionCatalog.RequestsReadAll, cancellationToken);
        var readOwn = await permissions.GetSiteScopeAsync(user, PermissionCatalog.RequestsReadOwn, cancellationToken);

        if (userId is null || (!readAll.AllSites && readAll.SiteIds.Count == 0 && readOwn.SiteIds.Count == 0))
        {
            return Results.Ok(Array.Empty<RequestResponse>());
        }

        IQueryable<MaintenanceRequest> query = requestsDb.Requests.AsNoTracking();
        if (!readAll.AllSites)
        {
            var fullVisibilitySiteIds = readAll.SiteIds;
            var ownVisibilitySiteIds = readOwn.SiteIds;
            query = query.Where(request =>
                fullVisibilitySiteIds.Contains(request.SiteId) ||
                (ownVisibilitySiteIds.Contains(request.SiteId) && request.CreatedByUserId == userId));
        }

        if (siteId is not null)
        {
            if (!readAll.Includes(siteId.Value) && !readOwn.Includes(siteId.Value))
            {
                return Results.Ok(Array.Empty<RequestResponse>());
            }

            query = query.Where(request => request.SiteId == siteId.Value);
        }

        var list = await query.OrderByDescending(request => request.CreatedAtUtc).ToListAsync(cancellationToken);
        return Results.Ok(list.Select(RequestResponse.From));
    }

    private static async Task<IResult> GetRequestAsync(
        Guid id,
        ClaimsPrincipal user,
        MaintenanceRequestsDbContext requestsDb,
        IPermissionEvaluator permissions,
        CancellationToken cancellationToken)
    {
        var request = await requestsDb.Requests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null)
        {
            return Results.NotFound();
        }

        var userId = permissions.GetUserId(user);
        var canReadAll = await permissions.HasPermissionAsync(user, PermissionCatalog.RequestsReadAll, request.SiteId, cancellationToken);
        var canReadOwn = userId == request.CreatedByUserId &&
            await permissions.HasPermissionAsync(user, PermissionCatalog.RequestsReadOwn, request.SiteId, cancellationToken);

        if (!canReadAll && !canReadOwn)
        {
            // Not-found and forbidden look identical (docs/02): a resource id never confirms
            // existence to a caller who isn't authorized to see it.
            return Results.NotFound();
        }

        return Results.Ok(RequestResponse.From(request));
    }

    private static async Task<IResult> CreateRequestAsync(
        CreateRequestRequest request,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        MaintenanceRequestsDbContext requestsDb,
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

        if (request.AssetId is null && request.LocationId is null)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["asset"] = ["Either an asset or a location is required."]
            });
        }

        if (!await permissions.HasPermissionAsync(httpContext.User, PermissionCatalog.RequestsCreate, request.SiteId, cancellationToken))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Not permitted to submit requests at this site.");
        }

        if (request.AssetId is not null)
        {
            var assetInSameSite = await assetsDb.Assets
                .AnyAsync(asset => asset.Id == request.AssetId && asset.SiteId == request.SiteId, cancellationToken);
            if (!assetInSameSite)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["assetId"] = ["Asset must belong to the same site."]
                });
            }
        }

        if (request.LocationId is not null)
        {
            var locationInSameSite = await assetsDb.Locations
                .AnyAsync(location => location.Id == request.LocationId && location.SiteId == request.SiteId, cancellationToken);
            if (!locationInSameSite)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["locationId"] = ["Location must belong to the same site."]
                });
            }
        }

        var actorUserId = permissions.GetUserId(httpContext.User)
            ?? throw new InvalidOperationException("Authenticated request is missing a user id claim.");

        var maintenanceRequest = new MaintenanceRequest(
            request.SiteId,
            actorUserId,
            request.Title,
            request.Description,
            request.AssetId,
            request.LocationId,
            request.Priority);

        requestsDb.Requests.Add(maintenanceRequest);
        await requestsDb.SaveChangesAsync(cancellationToken);

        return Results.Created($"/requests/{maintenanceRequest.Id}", RequestResponse.From(maintenanceRequest));
    }

    private static async Task<IResult> ConvertRequestAsync(
        Guid id,
        ConvertRequestRequest request,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        IConfiguration configuration,
        IPermissionEvaluator permissions,
        IAuditEventWriter auditWriter,
        CancellationToken cancellationToken)
    {
        if (!await AntiforgeryHelpers.HasValidAntiforgeryTokenAsync(httpContext, antiforgery))
        {
            return Results.BadRequest(new { error = "Invalid anti-forgery token." });
        }

        var connectionString = configuration.GetConnectionString("Cmms")
            ?? throw new InvalidOperationException("Connection string 'Cmms' is not configured.");

        await using var transactionScope = await SharedTransactionScope.BeginAsync(connectionString, cancellationToken);
        await using var requestsDb = transactionScope.CreateContext<MaintenanceRequestsDbContext>(options => new MaintenanceRequestsDbContext(options));
        await using var workOrdersDb = transactionScope.CreateContext<WorkManagementDbContext>(options => new WorkManagementDbContext(options));
        await using var auditDb = transactionScope.CreateContext<AuditDbContext>(options => new AuditDbContext(options));

        var maintenanceRequest = await requestsDb.Requests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (maintenanceRequest is null)
        {
            return Results.NotFound();
        }

        if (!await permissions.HasPermissionAsync(httpContext.User, PermissionCatalog.RequestsConvert, maintenanceRequest.SiteId, cancellationToken))
        {
            return Results.NotFound();
        }

        if (maintenanceRequest.Status != MaintenanceRequestStatus.New)
        {
            return Results.Conflict(new { error = $"Only a New request can be converted (this one is {maintenanceRequest.Status})." });
        }

        var actorUserId = permissions.GetUserId(httpContext.User)
            ?? throw new InvalidOperationException("Authenticated request is missing a user id claim.");

        var workOrder = new WorkOrder(
            maintenanceRequest.SiteId,
            string.IsNullOrWhiteSpace(request.Title) ? maintenanceRequest.Title : request.Title,
            maintenanceRequest.Description,
            maintenanceRequest.AssetId,
            maintenanceRequest.LocationId,
            actorUserId,
            ToWorkOrderPriority(maintenanceRequest.Priority),
            maintenanceRequest.Id);
        workOrdersDb.WorkOrders.Add(workOrder);
        await workOrdersDb.SaveChangesAsync(cancellationToken);

        // Conditional, atomic — the actual guard against a race with a concurrent Reject/Cancel on
        // the same request (docs/01 "Resolves QA finding B-04(1)"), not the AsNoTracking read above.
        var affectedRows = await requestsDb.Database.ExecuteSqlInterpolatedAsync(
            $"""
             UPDATE maintenance_requests.requests
             SET status = 'Converted', converted_work_order_id = {workOrder.Id}, resolved_at_utc = now(), row_version = row_version + 1
             WHERE id = {id} AND status = 'New'
             """,
            cancellationToken);

        if (affectedRows == 0)
        {
            await transactionScope.RollbackAsync(cancellationToken);
            return Results.Conflict(new { error = "This request was already resolved by another action." });
        }

        await auditWriter.WriteAsync(
            auditDb,
            new AuditEventEntry(
                ActorUserId: actorUserId,
                Action: "request.converted",
                ResourceType: "MaintenanceRequest",
                ResourceId: maintenanceRequest.Id,
                SiteId: maintenanceRequest.SiteId,
                CorrelationId: workOrder.Id,
                Reason: null,
                BeforeJson: JsonSerializer.Serialize(new { status = "New" }),
                AfterJson: JsonSerializer.Serialize(new { status = "Converted", workOrderId = workOrder.Id })),
            cancellationToken);
        await auditWriter.WriteAsync(
            auditDb,
            new AuditEventEntry(
                ActorUserId: actorUserId,
                Action: "workorder.created",
                ResourceType: "WorkOrder",
                ResourceId: workOrder.Id,
                SiteId: workOrder.SiteId,
                CorrelationId: maintenanceRequest.Id,
                Reason: null,
                BeforeJson: null,
                AfterJson: JsonSerializer.Serialize(new { status = workOrder.Status.ToString(), sourceRequestId = maintenanceRequest.Id })),
            cancellationToken);

        await transactionScope.CommitAsync(cancellationToken);

        return Results.Ok(new ConvertRequestResponse(workOrder.Id));
    }

    private static async Task<IResult> RejectRequestAsync(
        Guid id,
        ReasonRequest request,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        IConfiguration configuration,
        IPermissionEvaluator permissions,
        IAuditEventWriter auditWriter,
        CancellationToken cancellationToken)
    {
        if (!await AntiforgeryHelpers.HasValidAntiforgeryTokenAsync(httpContext, antiforgery))
        {
            return Results.BadRequest(new { error = "Invalid anti-forgery token." });
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["reason"] = ["A reason is required to reject a request."]
            });
        }

        return await ResolveAsync(
            id,
            httpContext,
            configuration,
            permissions,
            auditWriter,
            PermissionCatalog.RequestsReject,
            requiresOwnership: false,
            targetStatus: "Rejected",
            action: "request.rejected",
            reason: request.Reason,
            cancellationToken);
    }

    private static async Task<IResult> CancelRequestAsync(
        Guid id,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        IConfiguration configuration,
        IPermissionEvaluator permissions,
        IAuditEventWriter auditWriter,
        CancellationToken cancellationToken)
    {
        if (!await AntiforgeryHelpers.HasValidAntiforgeryTokenAsync(httpContext, antiforgery))
        {
            return Results.BadRequest(new { error = "Invalid anti-forgery token." });
        }

        return await ResolveAsync(
            id,
            httpContext,
            configuration,
            permissions,
            auditWriter,
            PermissionCatalog.RequestsCancelOwn,
            requiresOwnership: true,
            targetStatus: "Cancelled",
            action: "request.cancelled",
            reason: null,
            cancellationToken);
    }

    /// <summary>
    /// Shared implementation for Reject and Cancel — both are "New -> terminal" atomic conditional
    /// updates that differ only in target status, the permission checked, and whether the caller
    /// must also be the request's own creator (<c>requests.cancel.own</c> has no "any" counterpart
    /// in <see cref="PermissionCatalog"/> — every role, Admin/Planner included, can only cancel
    /// their own New request; Planner/Admin decline someone else's via Reject instead).
    /// </summary>
    private static async Task<IResult> ResolveAsync(
        Guid id,
        HttpContext httpContext,
        IConfiguration configuration,
        IPermissionEvaluator permissions,
        IAuditEventWriter auditWriter,
        string permissionCode,
        bool requiresOwnership,
        string targetStatus,
        string action,
        string? reason,
        CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("Cmms")
            ?? throw new InvalidOperationException("Connection string 'Cmms' is not configured.");

        await using var transactionScope = await SharedTransactionScope.BeginAsync(connectionString, cancellationToken);
        await using var requestsDb = transactionScope.CreateContext<MaintenanceRequestsDbContext>(options => new MaintenanceRequestsDbContext(options));
        await using var auditDb = transactionScope.CreateContext<AuditDbContext>(options => new AuditDbContext(options));

        var maintenanceRequest = await requestsDb.Requests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (maintenanceRequest is null)
        {
            return Results.NotFound();
        }

        var actorUserId = permissions.GetUserId(httpContext.User);
        if (actorUserId is null || !await permissions.HasPermissionAsync(httpContext.User, permissionCode, maintenanceRequest.SiteId, cancellationToken))
        {
            return Results.NotFound();
        }

        if (requiresOwnership && maintenanceRequest.CreatedByUserId != actorUserId)
        {
            return Results.NotFound();
        }

        if (maintenanceRequest.Status != MaintenanceRequestStatus.New)
        {
            return Results.Conflict(new { error = $"Only a New request can be resolved this way (this one is {maintenanceRequest.Status})." });
        }

        var affectedRows = requiresOwnership
            ? await requestsDb.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 UPDATE maintenance_requests.requests
                 SET status = {targetStatus}::text, resolved_at_utc = now(), row_version = row_version + 1
                 WHERE id = {id} AND status = 'New' AND created_by_user_id = {actorUserId}
                 """,
                cancellationToken)
            : await requestsDb.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 UPDATE maintenance_requests.requests
                 SET status = {targetStatus}::text, rejected_reason = {reason}, resolved_at_utc = now(), row_version = row_version + 1
                 WHERE id = {id} AND status = 'New'
                 """,
                cancellationToken);

        if (affectedRows == 0)
        {
            await transactionScope.RollbackAsync(cancellationToken);
            return Results.Conflict(new { error = "This request was already resolved by another action." });
        }

        await auditWriter.WriteAsync(
            auditDb,
            new AuditEventEntry(
                ActorUserId: actorUserId,
                Action: action,
                ResourceType: "MaintenanceRequest",
                ResourceId: maintenanceRequest.Id,
                SiteId: maintenanceRequest.SiteId,
                CorrelationId: null,
                Reason: reason,
                BeforeJson: JsonSerializer.Serialize(new { status = "New" }),
                AfterJson: JsonSerializer.Serialize(new { status = targetStatus })),
            cancellationToken);

        await transactionScope.CommitAsync(cancellationToken);

        return Results.Ok(new { id, status = targetStatus });
    }

    /// <summary>
    /// RequestPriority and WorkOrderPriority are deliberately separate enums (schema-per-module
    /// boundary — see RequestPriority's doc comment), so converting a Request's priority onto its
    /// converted Work Order needs an explicit mapping rather than an int cast that would silently
    /// break if either enum's member order ever diverges.
    /// </summary>
    private static WorkOrderPriority ToWorkOrderPriority(RequestPriority priority) => priority switch
    {
        RequestPriority.P1 => WorkOrderPriority.P1,
        RequestPriority.P2 => WorkOrderPriority.P2,
        RequestPriority.P3 => WorkOrderPriority.P3,
        RequestPriority.P4 => WorkOrderPriority.P4,
        _ => throw new ArgumentOutOfRangeException(nameof(priority), priority, "Unknown request priority.")
    };
}

// ---------- Requests ----------

internal sealed record CreateRequestRequest(
    Guid SiteId,
    string Title,
    string? Description,
    Guid? AssetId,
    Guid? LocationId,
    RequestPriority Priority = RequestPriority.P3);

internal sealed record ConvertRequestRequest(string? Title);

internal sealed record ReasonRequest(string Reason);

// ---------- Responses ----------

internal sealed record RequestResponse(
    Guid Id,
    Guid SiteId,
    Guid CreatedByUserId,
    string Title,
    string? Description,
    Guid? AssetId,
    Guid? LocationId,
    RequestPriority Priority,
    MaintenanceRequestStatus Status,
    Guid? ConvertedWorkOrderId,
    string? RejectedReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ResolvedAtUtc,
    long RowVersion)
{
    public static RequestResponse From(MaintenanceRequest request) => new(
        request.Id,
        request.SiteId,
        request.CreatedByUserId,
        request.Title,
        request.Description,
        request.AssetId,
        request.LocationId,
        request.Priority,
        request.Status,
        request.ConvertedWorkOrderId,
        request.RejectedReason,
        request.CreatedAtUtc,
        request.ResolvedAtUtc,
        request.RowVersion);
}

internal sealed record ConvertRequestResponse(Guid WorkOrderId);
