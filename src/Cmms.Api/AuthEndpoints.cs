using System.Security.Claims;
using Cmms.Modules.IdentityAccess.Domain;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;

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

    private static IResult Me(ClaimsPrincipal principal) =>
        Results.Ok(new
        {
            id = principal.FindFirstValue(ClaimTypes.NameIdentifier),
            email = principal.FindFirstValue(ClaimTypes.Email) ?? principal.Identity?.Name
        });

    private sealed record LoginRequest(string Email, string Password);
}
