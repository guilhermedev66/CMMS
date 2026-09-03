using System.Security.Claims;
using Cmms.Modules.IdentityAccess.Domain;
using Cmms.Modules.IdentityAccess.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Cmms.Modules.IdentityAccess.Application;

/// <summary>
/// The result of asking "which sites can this caller exercise this permission in".
/// <see cref="AllSites"/> is true only for company-wide Admin, per
/// docs/02-security-and-invariants.md: "'site' for Admin is implicitly all sites; for every
/// other role it is the acting user's actual site memberships for the specific site the target
/// resource belongs to."
/// </summary>
public sealed record PermissionSiteScope(bool AllSites, IReadOnlyCollection<Guid> SiteIds)
{
    public bool Includes(Guid siteId) => AllSites || SiteIds.Contains(siteId);
}

/// <summary>
/// First real consumer of <see cref="PermissionCatalog"/> beyond login. Every check here is
/// `permission + site + resource-predicate`, matching docs/02-security-and-invariants.md's atomic
/// permission table 1:1 — never permission alone.
///
/// Design note (why this is an explicit helper called from each endpoint, not a declarative
/// ASP.NET Core <c>[Authorize(Policy = ...)]</c> attribute): the table's "site" scope is the
/// *resource's own* site_id, which for a single-resource operation (get/edit/change-criticality)
/// is only known after loading that resource — a generic policy handler would still need the
/// endpoint to hand it the loaded resource to do a resource-based check, which is the same amount
/// of code as calling this evaluator directly, minus the indirection. Calling it explicitly keeps
/// each endpoint's authorization line traceable 1:1 back to a specific row in that table, which is
/// exactly what docs/02's threat-model row for "Authz bypass" asks a future audit to be able to
/// verify ("an automated test asserting every endpoint has an explicit policy *and* that the
/// policy matches the atomic-operation table above").
///
/// Every check queries live DB state (no cached role/membership claim), so a membership revoked
/// mid-session loses authority on the caller's very next request — this is the "re-validates that
/// membership is still active ... not only at request start" requirement, satisfied by simply
/// never caching it in the first place rather than by a claims-invalidation mechanism.
/// </summary>
public interface IPermissionEvaluator
{
    Task<bool> HasPermissionAsync(
        ClaimsPrincipal principal,
        string permissionCode,
        Guid targetSiteId,
        CancellationToken cancellationToken = default);

    /// <summary>For company-global permissions (users.manage, sites.manage) — Admin only.</summary>
    Task<bool> HasCompanyGlobalPermissionAsync(
        ClaimsPrincipal principal,
        string permissionCode,
        CancellationToken cancellationToken = default);

    Task<PermissionSiteScope> GetSiteScopeAsync(
        ClaimsPrincipal principal,
        string permissionCode,
        CancellationToken cancellationToken = default);

    /// <summary>Admin (company-wide) or the caller's active site-membership role at <paramref name="siteId"/>, else null.</summary>
    Task<RoleCode?> GetEffectiveRoleAsync(
        ClaimsPrincipal principal,
        Guid siteId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch form of <see cref="GetEffectiveRoleAsync"/> used when rendering a list spanning
    /// several sites (e.g. Admin listing assets across sites), so the caller can pick the right
    /// field-visibility projection per row (e.g. Requester's "limited_asset_fields" predicate)
    /// without one round trip per row.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, RoleCode>> GetSiteRolesAsync(
        ClaimsPrincipal principal,
        IReadOnlyCollection<Guid> siteIds,
        CancellationToken cancellationToken = default);

    Guid? GetUserId(ClaimsPrincipal principal);
}

public sealed class PermissionEvaluator(IdentityAccessDbContext dbContext) : IPermissionEvaluator
{
    public async Task<bool> HasPermissionAsync(
        ClaimsPrincipal principal,
        string permissionCode,
        Guid targetSiteId,
        CancellationToken cancellationToken = default)
    {
        var userId = GetUserId(principal);
        if (userId is null)
        {
            return false;
        }

        if (await IsAdminAsync(userId.Value, cancellationToken))
        {
            return true;
        }

        return await dbContext.SiteMemberships
            .Where(membership => membership.UserId == userId && membership.IsActive && membership.SiteId == targetSiteId)
            .Join(
                dbContext.RolePermissions,
                membership => membership.RoleCode,
                rolePermission => rolePermission.RoleCode,
                (membership, rolePermission) => rolePermission)
            .AnyAsync(rolePermission => rolePermission.PermissionCode == permissionCode, cancellationToken);
    }

    public async Task<bool> HasCompanyGlobalPermissionAsync(
        ClaimsPrincipal principal,
        string permissionCode,
        CancellationToken cancellationToken = default)
    {
        var userId = GetUserId(principal);
        return userId is not null && await IsAdminAsync(userId.Value, cancellationToken);
    }

    public async Task<PermissionSiteScope> GetSiteScopeAsync(
        ClaimsPrincipal principal,
        string permissionCode,
        CancellationToken cancellationToken = default)
    {
        var userId = GetUserId(principal);
        if (userId is null)
        {
            return new PermissionSiteScope(false, []);
        }

        if (await IsAdminAsync(userId.Value, cancellationToken))
        {
            return new PermissionSiteScope(true, []);
        }

        var siteIds = await dbContext.SiteMemberships
            .Where(membership => membership.UserId == userId && membership.IsActive)
            .Join(
                dbContext.RolePermissions,
                membership => membership.RoleCode,
                rolePermission => rolePermission.RoleCode,
                (membership, rolePermission) => new { membership.SiteId, rolePermission.PermissionCode })
            .Where(joined => joined.PermissionCode == permissionCode)
            .Select(joined => joined.SiteId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return new PermissionSiteScope(false, siteIds);
    }

    public async Task<RoleCode?> GetEffectiveRoleAsync(
        ClaimsPrincipal principal,
        Guid siteId,
        CancellationToken cancellationToken = default)
    {
        var userId = GetUserId(principal);
        if (userId is null)
        {
            return null;
        }

        if (await IsAdminAsync(userId.Value, cancellationToken))
        {
            return RoleCode.Admin;
        }

        var membership = await dbContext.SiteMemberships
            .Where(m => m.UserId == userId && m.SiteId == siteId && m.IsActive)
            .Select(m => (RoleCode?)m.RoleCode)
            .FirstOrDefaultAsync(cancellationToken);

        return membership;
    }

    public async Task<IReadOnlyDictionary<Guid, RoleCode>> GetSiteRolesAsync(
        ClaimsPrincipal principal,
        IReadOnlyCollection<Guid> siteIds,
        CancellationToken cancellationToken = default)
    {
        var userId = GetUserId(principal);
        if (userId is null || siteIds.Count == 0)
        {
            return new Dictionary<Guid, RoleCode>();
        }

        if (await IsAdminAsync(userId.Value, cancellationToken))
        {
            return siteIds.Distinct().ToDictionary(siteId => siteId, _ => RoleCode.Admin);
        }

        return await dbContext.SiteMemberships
            .Where(membership => membership.UserId == userId && membership.IsActive && siteIds.Contains(membership.SiteId))
            .ToDictionaryAsync(membership => membership.SiteId, membership => membership.RoleCode, cancellationToken);
    }

    public Guid? GetUserId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var userId) ? userId : null;
    }

    private Task<bool> IsAdminAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.CompanyRoleAssignments
            .AnyAsync(assignment => assignment.UserId == userId && assignment.RoleCode == RoleCode.Admin, cancellationToken);
}
