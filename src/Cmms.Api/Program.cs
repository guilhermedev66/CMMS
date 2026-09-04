using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Cmms.Api;
using Cmms.Api.Realtime;
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
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Render "Secret Files" (as opposed to plain env vars) are mounted read-only under /etc/secrets,
// one file per secret, filename == the config key with "__" as the section separator — the same
// convention .NET env-var binding uses. This is the deploy-time secret channel for this service
// (see render.yaml / README's Deployment section): each file's content becomes the value at that
// config path, added last so it overrides both appsettings.json and any plain env vars.
const string secretsDirectory = "/etc/secrets";
if (Directory.Exists(secretsDirectory))
{
    var secretOverrides = Directory.GetFiles(secretsDirectory)
        .Select(path => new KeyValuePair<string, string?>(
            Path.GetFileName(path).Replace("__", ":"),
            File.ReadAllText(path).Trim()))
        .ToArray();
    builder.Configuration.AddInMemoryCollection(secretOverrides);
}

var secureCookiePolicy = builder.Configuration.GetValue("Authentication:RequireSecureCookies", true)
    ? CookieSecurePolicy.Always
    : CookieSecurePolicy.SameAsRequest;

// ADR-16: OpenTelemetry, kept deliberately small — ASP.NET Core request tracing/metrics plus
// outbound HttpClient calls (there are none of substance yet, but this is the near-zero-cost-now
// the ADR argues for). An OTLP exporter (any standard collector — Grafana Cloud, Honeycomb, a
// self-hosted collector, etc.) activates when Otel:OtlpEndpoint is configured, so this feature is
// never "on" only if you've signed up for a specific vendor. The console exporter is separately
// toggled (default on) — CmmsWebApplicationFactory turns it off for the integration-test suite,
// where every one of hundreds of HTTP calls per run would otherwise dump a multi-line span/metric
// block to the test log for no one to ever read.
var otlpEndpoint = builder.Configuration["Otel:OtlpEndpoint"];
var consoleExporterEnabled = builder.Configuration.GetValue("Otel:ConsoleExporterEnabled", true);
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(serviceName: "cmms-api"))
    .WithTracing(tracing =>
    {
        tracing
            .AddAspNetCoreInstrumentation(options =>
            {
                // The health check is polled constantly by orchestrators/load balancers — tracing
                // every hit is pure noise, not a signal anyone will ever look at.
                options.Filter = context => context.Request.Path != "/health";
            })
            .AddHttpClientInstrumentation()
            // Npgsql emits its own ActivitySource ("Npgsql") natively (7.0+) — no separate
            // EF Core/Npgsql instrumentation package needed, just subscribe to the source.
            .AddSource("Npgsql");
        if (consoleExporterEnabled)
        {
            tracing.AddConsoleExporter();
        }
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otlpEndpoint));
        }
    })
    .WithMetrics(metrics =>
    {
        metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();
        if (consoleExporterEnabled)
        {
            metrics.AddConsoleExporter();
        }
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            metrics.AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otlpEndpoint));
        }
    });
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
// ADR-17: SignalR bounded to the M5 dispatch board only.
builder.Services.AddSignalR();
builder.Services.AddSingleton<WorkOrderDispatchBroadcaster>();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = antiforgeryCookieName;
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = secureCookiePolicy;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.Path = "/";
});

// docs/02's threat-model row: "Rate-limit login, request-submission, uploads, and exports." A
// global partitioned limiter (by authenticated user id when present, else remote IP — so one
// abusive anonymous IP can't exhaust another user's budget and vice versa) covers every endpoint
// by default; "/auth/login" additionally carries its own tighter, IP-only policy (see
// AuthEndpoints.cs) since credential-guessing is worth slowing down harder than ordinary
// authenticated-write abuse. Both use a sliding window so a burst right at the window boundary
// can't double the effective rate.
// Configurable (not hardcoded) specifically so the integration test suite — which drives dozens of
// logins/writes from what TestServer reports as a single shared loopback "IP" across many parallel
// test classes — can raise these to effectively-unlimited via CmmsWebApplicationFactory's env vars,
// the same pattern already used there for the scheduler/bootstrap-admin toggles, rather than the
// tests fighting production-realistic limits that have nothing to do with what they're asserting.
var globalPermitLimit = builder.Configuration.GetValue("RateLimiting:GlobalPermitLimit", 120);
var authPermitLimit = builder.Configuration.GetValue("RateLimiting:AuthPermitLimit", 10);
var uploadsPermitLimit = builder.Configuration.GetValue("RateLimiting:UploadsPermitLimit", 20);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        // SignalR's long-polling/WebSocket transport makes many small requests per connection by
        // design (not the "abuse" this limiter targets) — exempt the hub path itself, same as a
        // health check would be.
        if (httpContext.Request.Path.StartsWithSegments("/hubs") || httpContext.Request.Path.StartsWithSegments("/health"))
        {
            return RateLimitPartition.GetNoLimiter("exempt");
        }

        var partitionKey = httpContext.User.Identity?.IsAuthenticated == true
            ? $"user:{httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value}"
            : $"ip:{httpContext.Connection.RemoteIpAddress}";

        return RateLimitPartition.GetSlidingWindowLimiter(partitionKey, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = globalPermitLimit,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 4,
            QueueLimit = 0
        });
    });

    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetSlidingWindowLimiter(
            $"auth-ip:{httpContext.Connection.RemoteIpAddress}",
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = authPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 4,
                QueueLimit = 0
            }));

    // 20 x 15MB (HardMaxBytes in AttachmentsEndpoints.cs) = 300MB/min ceiling per user — still
    // generous for real technician evidence-photo usage, but well short of the ~1.8GB/min the
    // shared global budget alone would allow (docs/02 names "uploads" as its own rate-limit target,
    // not just an instance of ordinary write traffic).
    options.AddPolicy("uploads", httpContext =>
    {
        var partitionKey = httpContext.User.Identity?.IsAuthenticated == true
            ? $"user:{httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value}"
            : $"ip:{httpContext.Connection.RemoteIpAddress}";

        return RateLimitPartition.GetSlidingWindowLimiter($"uploads:{partitionKey}", _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = uploadsPermitLimit,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 4,
            QueueLimit = 0
        });
    });
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

// docs/02 / M6 hardening: a small, fixed set of response headers applied to every response, not
// just attachment downloads (which already set their own nosniff — see AttachmentsEndpoints.cs).
// No CSP `unsafe-inline` allowance and no third-party frame ancestors — this API serves JSON/files
// to its own SPA only, never HTML a browser would render as a page.
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "same-origin");
    context.Response.Headers.Append("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'");
    if (secureCookiePolicy == CookieSecurePolicy.Always)
    {
        context.Response.Headers.Append("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
    }

    await next();
});

app.UseRateLimiter();
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
app.MapReportingEndpoints();
app.MapHub<WorkOrderDispatchHub>("/hubs/work-orders");

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
