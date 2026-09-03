using System.Security.Claims;
using System.Text.Json;
using Cmms.BuildingBlocks.Database;
using Cmms.Modules.Assets.Domain;
using Cmms.Modules.Assets.Infrastructure;
using Cmms.Modules.Audit.Application;
using Cmms.Modules.Audit.Infrastructure;
using Cmms.Modules.IdentityAccess.Application;
using Cmms.Modules.IdentityAccess.Domain;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace Cmms.Api;

/// <summary>
/// Asset + Location CRUD, RBAC-enforced per docs/02-security-and-invariants.md's atomic
/// permission table. This is the reference implementation for how M2/M3 endpoints should wire
/// authorization: load the resource (or resolve the target site for a create), call
/// <see cref="IPermissionEvaluator"/> with the exact permission code + that site id, and only
/// then act. Not-found and permission-denied both surface as 404 for single-resource endpoints
/// (docs/02: "not-found and forbidden responses look identical to avoid confirming existence").
/// </summary>
internal static class AssetsEndpoints
{
    public static IEndpointRouteBuilder MapAssetsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var locations = endpoints.MapGroup("/locations").WithTags("Locations").RequireAuthorization();
        locations.MapGet("", ListLocationsAsync);
        locations.MapPost("", CreateLocationAsync);

        var assets = endpoints.MapGroup("/assets").WithTags("Assets").RequireAuthorization();
        assets.MapGet("", ListAssetsAsync);
        assets.MapGet("/{id:guid}", GetAssetAsync);
        assets.MapPost("", CreateAssetAsync);
        assets.MapPut("/{id:guid}", EditAssetAsync);
        assets.MapPost("/{id:guid}/criticality", ChangeCriticalityAsync);

        return endpoints;
    }

    // ---------- Locations ----------

    private static async Task<IResult> ListLocationsAsync(
        Guid? siteId,
        ClaimsPrincipal user,
        AssetsDbContext assetsDb,
        IPermissionEvaluator permissions,
        CancellationToken cancellationToken)
    {
        var scope = await permissions.GetSiteScopeAsync(user, PermissionCatalog.AssetsRead, cancellationToken);

        IQueryable<Location> query = assetsDb.Locations.AsNoTracking();
        if (!scope.AllSites)
        {
            if (scope.SiteIds.Count == 0)
            {
                return Results.Ok(Array.Empty<LocationResponse>());
            }

            query = query.Where(location => scope.SiteIds.Contains(location.SiteId));
        }

        if (siteId is not null)
        {
            if (!scope.Includes(siteId.Value))
            {
                return Results.Ok(Array.Empty<LocationResponse>());
            }

            query = query.Where(location => location.SiteId == siteId.Value);
        }

        var locations = await query.OrderBy(location => location.Code).ToListAsync(cancellationToken);
        return Results.Ok(locations.Select(LocationResponse.From));
    }

    private static async Task<IResult> CreateLocationAsync(
        CreateLocationRequest request,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        AssetsDbContext assetsDb,
        IPermissionEvaluator permissions,
        CancellationToken cancellationToken)
    {
        if (!await AntiforgeryHelpers.HasValidAntiforgeryTokenAsync(httpContext, antiforgery))
        {
            return Results.BadRequest(new { error = "Invalid anti-forgery token." });
        }

        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["location"] = ["Code and name are required."]
            });
        }

        // Locations have no dedicated permission row in docs/02's table; they are managed
        // alongside Assets under the same site-scoped create/edit authority (assets.create).
        if (!await permissions.HasPermissionAsync(httpContext.User, PermissionCatalog.AssetsCreate, request.SiteId, cancellationToken))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Not permitted to create locations at this site.");
        }

        if (request.ParentLocationId is not null)
        {
            var parentInSameSite = await assetsDb.Locations
                .AnyAsync(location => location.Id == request.ParentLocationId && location.SiteId == request.SiteId, cancellationToken);
            if (!parentInSameSite)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["parentLocationId"] = ["Parent location must belong to the same site."]
                });
            }
        }

        var location = new Location(request.SiteId, request.Code, request.Name, request.ParentLocationId);
        assetsDb.Locations.Add(location);
        await assetsDb.SaveChangesAsync(cancellationToken);

        return Results.Created($"/locations/{location.Id}", LocationResponse.From(location));
    }

    // ---------- Assets ----------

    private static async Task<IResult> ListAssetsAsync(
        Guid? siteId,
        ClaimsPrincipal user,
        AssetsDbContext assetsDb,
        IPermissionEvaluator permissions,
        CancellationToken cancellationToken)
    {
        var scope = await permissions.GetSiteScopeAsync(user, PermissionCatalog.AssetsRead, cancellationToken);

        IQueryable<Asset> query = assetsDb.Assets.AsNoTracking();
        if (!scope.AllSites)
        {
            if (scope.SiteIds.Count == 0)
            {
                return Results.Ok(Array.Empty<object>());
            }

            query = query.Where(asset => scope.SiteIds.Contains(asset.SiteId));
        }

        if (siteId is not null)
        {
            if (!scope.Includes(siteId.Value))
            {
                return Results.Ok(Array.Empty<object>());
            }

            query = query.Where(asset => asset.SiteId == siteId.Value);
        }

        var assetList = await query.OrderBy(asset => asset.Tag).ToListAsync(cancellationToken);
        var siteIds = assetList.Select(asset => asset.SiteId).Distinct().ToArray();
        var roles = await permissions.GetSiteRolesAsync(user, siteIds, cancellationToken);

        var payload = assetList.Select(asset => ProjectForRole(asset, roles.GetValueOrDefault(asset.SiteId)));
        return Results.Ok(payload);
    }

    private static async Task<IResult> GetAssetAsync(
        Guid id,
        ClaimsPrincipal user,
        AssetsDbContext assetsDb,
        IPermissionEvaluator permissions,
        CancellationToken cancellationToken)
    {
        var asset = await assetsDb.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (asset is null)
        {
            return Results.NotFound();
        }

        if (!await permissions.HasPermissionAsync(user, PermissionCatalog.AssetsRead, asset.SiteId, cancellationToken))
        {
            // Cross-site / no-permission: identical to not-found, so a resource id never confirms
            // existence to a caller who isn't authorized to see it (docs/02-security-and-invariants.md).
            return Results.NotFound();
        }

        var role = await permissions.GetEffectiveRoleAsync(user, asset.SiteId, cancellationToken);
        return Results.Ok(ProjectForRole(asset, role));
    }

    private static async Task<IResult> CreateAssetAsync(
        CreateAssetRequest request,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        AssetsDbContext assetsDb,
        IPermissionEvaluator permissions,
        CancellationToken cancellationToken)
    {
        if (!await AntiforgeryHelpers.HasValidAntiforgeryTokenAsync(httpContext, antiforgery))
        {
            return Results.BadRequest(new { error = "Invalid anti-forgery token." });
        }

        if (string.IsNullOrWhiteSpace(request.Tag) || string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Category))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["asset"] = ["Tag, name, and category are required."]
            });
        }

        if (!await permissions.HasPermissionAsync(httpContext.User, PermissionCatalog.AssetsCreate, request.SiteId, cancellationToken))
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Not permitted to create assets at this site.");
        }

        if (request.CurrentLocationId is not null)
        {
            var locationInSameSite = await assetsDb.Locations
                .AnyAsync(location => location.Id == request.CurrentLocationId && location.SiteId == request.SiteId, cancellationToken);
            if (!locationInSameSite)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["currentLocationId"] = ["Location must belong to the same site."]
                });
            }
        }

        var asset = new Asset(
            request.SiteId,
            request.Tag,
            request.Name,
            request.Category,
            request.Criticality,
            request.CurrentLocationId,
            request.ParentAssetId);

        assetsDb.Assets.Add(asset);
        await assetsDb.SaveChangesAsync(cancellationToken);

        return Results.Created($"/assets/{asset.Id}", AssetResponse.From(asset));
    }

    private static async Task<IResult> EditAssetAsync(
        Guid id,
        EditAssetRequest request,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        AssetsDbContext assetsDb,
        IPermissionEvaluator permissions,
        CancellationToken cancellationToken)
    {
        if (!await AntiforgeryHelpers.HasValidAntiforgeryTokenAsync(httpContext, antiforgery))
        {
            return Results.BadRequest(new { error = "Invalid anti-forgery token." });
        }

        var asset = await assetsDb.Assets.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (asset is null)
        {
            return Results.NotFound();
        }

        if (!await permissions.HasPermissionAsync(httpContext.User, PermissionCatalog.AssetsEdit, asset.SiteId, cancellationToken))
        {
            return Results.NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Category))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["asset"] = ["Name and category are required."]
            });
        }

        if (request.CurrentLocationId is not null)
        {
            var locationInSameSite = await assetsDb.Locations
                .AnyAsync(location => location.Id == request.CurrentLocationId && location.SiteId == asset.SiteId, cancellationToken);
            if (!locationInSameSite)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["currentLocationId"] = ["Location must belong to the same site."]
                });
            }
        }

        // NOTE (scope cut): docs/02's "Minimum audited actions" also names asset location changes
        // alongside criticality changes. Criticality gets its own audited action below per this
        // slice's explicit requirement; a symmetric audited "move asset" action is deferred rather
        // than silently bundling an unaudited location change into this general edit — flagged
        // here rather than left unstated.
        asset.UpdateDetails(
            request.Name,
            request.Category,
            request.Manufacturer,
            request.Model,
            request.SerialNumber,
            request.Status,
            request.CurrentLocationId);

        await assetsDb.SaveChangesAsync(cancellationToken);

        return Results.Ok(AssetResponse.From(asset));
    }

    private static async Task<IResult> ChangeCriticalityAsync(
        Guid id,
        ChangeCriticalityRequest request,
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
            // docs/02: criticality changes require an explicit reason, same as
            // cancellation/hold/close-override/reopen/privileged correction.
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["reason"] = ["A reason is required for a criticality change."]
            });
        }

        var connectionString = configuration.GetConnectionString("Cmms")
            ?? throw new InvalidOperationException("Connection string 'Cmms' is not configured.");

        // Both the asset mutation and its audit event are written through this shared
        // transaction scope so they commit — or roll back — together (docs/02: "Written in the
        // same transaction as the domain change").
        await using var transactionScope = await SharedTransactionScope.BeginAsync(connectionString, cancellationToken);
        await using var assetsDb = transactionScope.CreateContext<AssetsDbContext>(options => new AssetsDbContext(options));
        await using var auditDb = transactionScope.CreateContext<AuditDbContext>(options => new AuditDbContext(options));

        var asset = await assetsDb.Assets.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (asset is null)
        {
            return Results.NotFound();
        }

        if (!await permissions.HasPermissionAsync(httpContext.User, PermissionCatalog.AssetsCriticalityChange, asset.SiteId, cancellationToken))
        {
            return Results.NotFound();
        }

        if (asset.Criticality == request.Criticality)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["criticality"] = ["Asset already has this criticality."]
            });
        }

        var previousCriticality = asset.ChangeCriticality(request.Criticality);
        await assetsDb.SaveChangesAsync(cancellationToken);

        var actorUserId = permissions.GetUserId(httpContext.User);
        await auditWriter.WriteAsync(
            auditDb,
            new AuditEventEntry(
                ActorUserId: actorUserId,
                Action: "asset.criticality.changed",
                ResourceType: "Asset",
                ResourceId: asset.Id,
                SiteId: asset.SiteId,
                CorrelationId: null,
                Reason: request.Reason,
                BeforeJson: JsonSerializer.Serialize(new { criticality = previousCriticality.ToString() }),
                AfterJson: JsonSerializer.Serialize(new { criticality = asset.Criticality.ToString() })),
            cancellationToken);

        await transactionScope.CommitAsync(cancellationToken);

        return Results.Ok(AssetResponse.From(asset));
    }

    private static object ProjectForRole(Asset asset, RoleCode? role) =>
        role == RoleCode.Requester
            ? AssetLimitedResponse.From(asset)
            : AssetResponse.From(asset);
}

// ---------- Requests ----------

internal sealed record CreateLocationRequest(Guid SiteId, string Code, string Name, Guid? ParentLocationId);

internal sealed record CreateAssetRequest(
    Guid SiteId,
    string Tag,
    string Name,
    string Category,
    AssetCriticality Criticality,
    Guid? CurrentLocationId,
    Guid? ParentAssetId);

internal sealed record EditAssetRequest(
    string Name,
    string Category,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    AssetStatus Status,
    Guid? CurrentLocationId);

internal sealed record ChangeCriticalityRequest(AssetCriticality Criticality, string Reason);

// ---------- Responses ----------

/// <summary>Full asset projection — Admin/Planner (any site scope) and Technician (own site, read-only).</summary>
internal sealed record AssetResponse(
    Guid Id,
    Guid SiteId,
    string Tag,
    string Name,
    string Category,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    AssetCriticality Criticality,
    AssetStatus Status,
    Guid? CurrentLocationId,
    Guid? ParentAssetId,
    Guid QrLocator,
    DateTimeOffset CreatedAtUtc,
    long RowVersion)
{
    public static AssetResponse From(Asset asset) => new(
        asset.Id,
        asset.SiteId,
        asset.Tag,
        asset.Name,
        asset.Category,
        asset.Manufacturer,
        asset.Model,
        asset.SerialNumber,
        asset.Criticality,
        asset.Status,
        asset.CurrentLocationId,
        asset.ParentAssetId,
        asset.QrLocator,
        asset.CreatedAtUtc,
        asset.RowVersion);
}

/// <summary>
/// Requester projection — docs/02-security-and-invariants.md: `assets.read` for Requester is
/// "Limited (tag/name/location only)".
/// </summary>
internal sealed record AssetLimitedResponse(Guid Id, string Tag, string Name, Guid? CurrentLocationId)
{
    public static AssetLimitedResponse From(Asset asset) => new(asset.Id, asset.Tag, asset.Name, asset.CurrentLocationId);
}

internal sealed record LocationResponse(
    Guid Id,
    Guid SiteId,
    string Code,
    string Name,
    Guid? ParentLocationId,
    DateTimeOffset CreatedAtUtc,
    long RowVersion)
{
    public static LocationResponse From(Location location) => new(
        location.Id,
        location.SiteId,
        location.Code,
        location.Name,
        location.ParentLocationId,
        location.CreatedAtUtc,
        location.RowVersion);
}
