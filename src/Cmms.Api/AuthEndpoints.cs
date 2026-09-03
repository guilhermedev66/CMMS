using System.Security.Claims;
using Cmms.Modules.IdentityAccess.Domain;
using Cmms.Modules.IdentityAccess.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Cmms.Api;

internal static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/auth").WithTags("Authentication");

        group.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { token = tokens.RequestToken });
        });

        group.MapPost("/login", LoginAsync);
        group.MapPost("/logout", LogoutAsync).RequireAuthorization();
        group.MapGet("/me", Me).RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext context,
        IAntiforgery antiforgery,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        if (!await AntiforgeryHelpers.HasValidAntiforgeryTokenAsync(context, antiforgery))
        {
            return Results.BadRequest(new { error = "Invalid anti-forgery token." });
        }

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["credentials"] = ["Email and password are required."]
            });
        }

        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive)
        {
            return Results.Unauthorized();
        }

        var result = await signInManager.PasswordSignInAsync(
            user,
            request.Password,
            isPersistent: false,
            lockoutOnFailure: true);

        return result.Succeeded ? Results.NoContent() : Results.Unauthorized();
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        if (!await AntiforgeryHelpers.HasValidAntiforgeryTokenAsync(context, antiforgery))
        {
            return Results.BadRequest(new { error = "Invalid anti-forgery token." });
        }

        var user = await userManager.GetUserAsync(context.User);
        if (user is not null)
        {
            await userManager.UpdateSecurityStampAsync(user);
        }

        await signInManager.SignOutAsync();
        return Results.NoContent();
    }

    /// <summary>
    /// Site memberships are included so the frontend can offer "which site is this for" on a
    /// create form (Request/Work Order creation requires a site id, per docs/01's site-boundness
    /// rule) without a separate "list my sites" round trip — there is no general-purpose sites API
    /// yet (sites.manage has no endpoint in this milestone), so this is the one place a client can
    /// learn its own site scope. Read-only: this never grants anything by itself, it only mirrors
    /// what IPermissionEvaluator already derives server-side on every write.
    /// </summary>
    private static async Task<IResult> Me(ClaimsPrincipal principal, IdentityAccessDbContext db, CancellationToken cancellationToken)
    {
        var rawId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(rawId, out var userId))
        {
            return Results.Unauthorized();
        }

        var isAdmin = await db.CompanyRoleAssignments
            .AnyAsync(assignment => assignment.UserId == userId && assignment.RoleCode == RoleCode.Admin, cancellationToken);

        var memberships = await db.SiteMemberships
            .Where(membership => membership.UserId == userId && membership.IsActive)
            .Join(db.Sites, membership => membership.SiteId, site => site.Id, (membership, site) => new
            {
                siteId = site.Id,
                siteName = site.Name,
                role = membership.RoleCode
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(new
        {
            id = rawId,
            email = principal.FindFirstValue(ClaimTypes.Email) ?? principal.Identity?.Name,
            isAdmin,
            siteMemberships = memberships
        });
    }

    private sealed record LoginRequest(string Email, string Password);
}
