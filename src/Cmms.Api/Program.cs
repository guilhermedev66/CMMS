using Cmms.Api;
using Cmms.Modules.Assets;
using Cmms.Modules.Assets.Infrastructure;
using Cmms.Modules.IdentityAccess;
using Cmms.Modules.IdentityAccess.Infrastructure;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var secureCookiePolicy = builder.Configuration.GetValue("Authentication:RequireSecureCookies", true)
    ? CookieSecurePolicy.Always
    : CookieSecurePolicy.SameAsRequest;
var antiforgeryCookieName = secureCookiePolicy == CookieSecurePolicy.Always
    ? "__Host-cmms-antiforgery"
    : "cmms-antiforgery";

builder.Services.AddIdentityAccess(builder.Configuration);
builder.Services.AddAssets(builder.Configuration);
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = antiforgeryCookieName;
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = secureCookiePolicy;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.Path = "/";
});

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Database:ApplyMigrations"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider
        .GetRequiredService<IdentityAccessDbContext>()
        .Database.MigrateAsync();
    await scope.ServiceProvider
        .GetRequiredService<AssetsDbContext>()
        .Database.MigrateAsync();
}

await IdentityAccessInitializer.BootstrapAdminAsync(app.Services, builder.Configuration);

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapAuthEndpoints();

app.MapGet("/health", async (
    IdentityAccessDbContext identityAccess,
    AssetsDbContext assets,
    CancellationToken cancellationToken) =>
{
    try
    {
        var databaseAvailable =
            await identityAccess.Database.CanConnectAsync(cancellationToken) &&
            await assets.Database.CanConnectAsync(cancellationToken);

        return databaseAvailable
            ? Results.Ok(new { status = "healthy" })
            : Results.Json(new { status = "unhealthy" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception)
    {
        return Results.Json(new { status = "unhealthy" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.Run();

public partial class Program;
