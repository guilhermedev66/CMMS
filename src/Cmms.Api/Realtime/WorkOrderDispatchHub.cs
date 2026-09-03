using Cmms.Modules.IdentityAccess.Domain;
using Cmms.Modules.IdentityAccess.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Cmms.Api.Realtime;

/// <summary>
/// The M5 dispatch board's live channel — ADR-17: "SignalR adopted, but bounded to the M5 dispatch
/// board (live Work Order board updates, emergency high-priority alerts), with server-derived
/// site-filtered group membership." Per docs/02-security-and-invariants.md's SignalR threat-model
/// row: "Group membership and every broadcast are server-derived and site-filtered from the
/// connection's authenticated identity, never from a client-supplied site/group parameter."
///
/// Group name is <c>site:{siteId}</c>. This hub never accepts a client-supplied site id to decide
/// group membership — <see cref="OnConnectedAsync"/> is the only place a connection is ever added
/// to a group, and it derives the set of sites entirely from the connected user's own active
/// <see cref="SiteMembership"/> rows (or every site, for a company-wide Admin) at connect time.
///
/// **Known gap, named rather than silently accepted**: this codebase has no
/// <c>users.manage</c>/<c>sites.manage</c> endpoint yet that revokes an existing
/// <see cref="SiteMembership"/> (per M2's docs, that endpoint was never built) — so "group
/// membership is revoked immediately on membership change" is currently vacuously true: there is no
/// code path that changes a membership out from under a live connection. When such an endpoint is
/// built, it must also force an affected connection to re-resolve its groups (e.g. by removing it
/// from the stale group directly via <see cref="IHubContext{THub}"/>, not just waiting for the next
/// reconnect) — tracked here as a follow-up, not silently ignored.
/// </summary>
[Microsoft.AspNetCore.Authorization.Authorize]
public sealed class WorkOrderDispatchHub(IdentityAccessDbContext identityAccessDb) : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userIdRaw = Context.UserIdentifier ?? Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdRaw, out var userId))
        {
            Context.Abort();
            return;
        }

        var isAdmin = await identityAccessDb.CompanyRoleAssignments
            .AnyAsync(assignment => assignment.UserId == userId && assignment.RoleCode == RoleCode.Admin);

        var siteIds = isAdmin
            ? await identityAccessDb.Sites.Select(site => site.Id).ToListAsync()
            : await identityAccessDb.SiteMemberships
                .Where(membership => membership.UserId == userId && membership.IsActive)
                .Select(membership => membership.SiteId)
                .Distinct()
                .ToListAsync();

        foreach (var siteId in siteIds)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(siteId));
        }

        await base.OnConnectedAsync();
    }

    public static string GroupName(Guid siteId) => $"site:{siteId}";
}

/// <summary>
/// Thin, intention-revealing wrapper around <see cref="IHubContext{WorkOrderDispatchHub}"/> so
/// endpoint code broadcasts by calling one clearly-named method instead of constructing a group
/// name and event name inline at every call site. Every broadcast method takes the resource's own
/// <c>siteId</c> as a plain parameter (not derived from any caller-supplied "which group to send
/// to" value) — the same server-derived-only property <see cref="WorkOrderDispatchHub"/> itself
/// upholds.
/// </summary>
public sealed class WorkOrderDispatchBroadcaster(IHubContext<WorkOrderDispatchHub> hubContext)
{
    public Task WorkOrderChangedAsync(Guid siteId, WorkOrderChangedPayload payload, CancellationToken cancellationToken = default) =>
        hubContext.Clients.Group(WorkOrderDispatchHub.GroupName(siteId)).SendAsync("WorkOrderChanged", payload, cancellationToken);

    public Task HighPriorityAlertAsync(Guid siteId, HighPriorityAlertPayload payload, CancellationToken cancellationToken = default) =>
        hubContext.Clients.Group(WorkOrderDispatchHub.GroupName(siteId)).SendAsync("HighPriorityAlert", payload, cancellationToken);
}

public sealed record WorkOrderChangedPayload(Guid WorkOrderId, Guid SiteId, string Status, string Priority, Guid? AssetId, string Action);

public sealed record HighPriorityAlertPayload(Guid WorkOrderId, Guid SiteId, string Title, string Priority, Guid? AssetId);
