using System.Text.Json.Serialization;
using Cmms.Api;
using Cmms.Modules.Assets;
using Cmms.Modules.Assets.Infrastructure;
using Cmms.Modules.Attachments;
using Cmms.Modules.Attachments.Infrastructure;
using Cmms.Modules.Audit;
using Cmms.Modules.Audit.Infrastructure;
using Cmms.Modules.IdentityAccess;
using Cmms.Modules.IdentityAccess.Infrastructure;
using Cmms.Modules.MaintenanceRequests;
using Cmms.Modules.MaintenanceRequests.Infrastructure;
using Cmms.Modules.PreventiveMaintenance;
using Cmms.Modules.PreventiveMaintenance.Infrastructure;
using Cmms.Modules.WorkManagement;
using Cmms.Modules.WorkManagement.Infrastructure;
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
builder.Services.AddAudit(builder.Configuration);
builder.Services.AddMaintenanceRequests(builder.Configuration);
builder.Services.AddWorkManagement(builder.Configuration);
builder.Services.AddPreventiveMaintenance(builder.Configuration);
builder.Services.AddAttachments(builder.Configuration);
builder.Services.AddScoped<IMaintenancePlanGenerationRunner, MaintenancePlanGenerationRunner>();
if (builder.Configuration.GetValue("PreventiveMaintenance:SchedulerEnabled", true))
{
    builder.Services.AddHostedService<MaintenancePlanGenerationService>();
}
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
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
    await scope.ServiceProvider
        .GetRequiredService<AuditDbContext>()
        .Database.MigrateAsync();
    await scope.ServiceProvider
        .GetRequiredService<MaintenanceRequestsDbContext>()
        .Database.MigrateAsync();
    await scope.ServiceProvider
        .GetRequiredService<WorkManagementDbContext>()
        .Database.MigrateAsync();
    await scope.ServiceProvider
        .GetRequiredService<PreventiveMaintenanceDbContext>()
        .Database.MigrateAsync();
    await scope.ServiceProvider
        .GetRequiredService<AttachmentsDbContext>()
        .Database.MigrateAsync();
}

await IdentityAccessInitializer.BootstrapAdminAsync(app.Services, builder.Configuration);

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapAuthEndpoints();
app.MapAssetsEndpoints();
app.MapMaintenanceRequestsEndpoints();
app.MapWorkOrdersEndpoints();
app.MapWorkOrderExecutionEndpoints();
app.MapAttachmentsEndpoints();
app.MapMaintenancePlansEndpoints();

app.MapGet("/health", async (
    IdentityAccessDbContext identityAccess,
    AssetsDbContext assets,
    AuditDbContext audit,
    MaintenanceRequestsDbContext maintenanceRequests,
    WorkManagementDbContext workManagement,
    PreventiveMaintenanceDbContext preventiveMaintenance,
    AttachmentsDbContext attachments,
    CancellationToken cancellationToken) =>
{
    try
    {
        var databaseAvailable =
            await identityAccess.Database.CanConnectAsync(cancellationToken) &&
            await assets.Database.CanConnectAsync(cancellationToken) &&
            await audit.Database.CanConnectAsync(cancellationToken) &&
            await maintenanceRequests.Database.CanConnectAsync(cancellationToken) &&
            await workManagement.Database.CanConnectAsync(cancellationToken) &&
            await preventiveMaintenance.Database.CanConnectAsync(cancellationToken) &&
            await attachments.Database.CanConnectAsync(cancellationToken);

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
