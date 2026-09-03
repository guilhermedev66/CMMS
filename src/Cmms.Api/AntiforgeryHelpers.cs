using Microsoft.AspNetCore.Antiforgery;

namespace Cmms.Api;

/// <summary>
/// Shared anti-forgery validation used by every state-changing endpoint, per
/// docs/02-security-and-invariants.md: "Anti-forgery token required on every state-changing
/// endpoint." Factored out of AuthEndpoints so Assets endpoints (and future modules) reuse the
/// same check instead of re-implementing it.
/// </summary>
internal static class AntiforgeryHelpers
{
    public static async Task<bool> HasValidAntiforgeryTokenAsync(HttpContext context, IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }
}
