using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Cmms.IntegrationTests;

/// <summary>
/// Boots the real Cmms.Api host (same Program.cs, same endpoint wiring, same DI composition)
/// against a real Testcontainers PostgreSQL instance instead of the Docker Compose one — this is
/// what makes the RBAC tests exercise the actual wired endpoints, not a reimplementation of the
/// permission check in test code.
///
/// Configuration is passed via process environment variables, not
/// <c>IWebHostBuilder.ConfigureAppConfiguration</c>: Program.cs reads
/// <c>builder.Configuration.GetConnectionString("Cmms")</c> synchronously while composing
/// services, before <c>builder.Build()</c> — which is the point at which
/// <c>WebApplicationFactory</c>'s own <c>ConfigureAppConfiguration</c> hook actually gets to
/// contribute to <c>builder.Configuration</c> for a minimal-hosting <c>Program.cs</c>. Env vars,
/// by contrast, are already present when <c>WebApplication.CreateBuilder(args)</c> runs its own
/// initial configuration pass, so they take effect in time — this is also exactly how
/// docker-compose.yml configures the real container (`ConnectionStrings__Cmms`, etc.), so the
/// test host is configured the same way a real deployment is.
/// </summary>
public sealed class CmmsWebApplicationFactory : WebApplicationFactory<Program>
{
    public CmmsWebApplicationFactory(string connectionString)
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Cmms", connectionString);
        // PostgresFixture already ran every module's migrations once for the shared container.
        Environment.SetEnvironmentVariable("Database__ApplyMigrations", "false");
        Environment.SetEnvironmentVariable("Authentication__RequireSecureCookies", "false");
        // No bootstrap admin here — each test seeds exactly the users/memberships it needs.
        Environment.SetEnvironmentVariable("BootstrapAdmin__Email", "");
        Environment.SetEnvironmentVariable("BootstrapAdmin__Password", "");
        // The real timer-driven sweep would run concurrently with, and independently of, whatever
        // a test is asserting — tests that need a sweep resolve IMaintenancePlanGenerationRunner
        // directly instead (see MaintenancePlanGenerationTests), which is also the more precise
        // simulation of "two ticks"/"two instances" than waiting on a real timer would be.
        Environment.SetEnvironmentVariable("PreventiveMaintenance__SchedulerEnabled", "false");
        // Rate limiting is production-real (Program.cs), but this suite drives dozens of logins
        // and writes from what TestServer reports as one shared loopback "IP" across many parallel
        // test classes — raise the limits here rather than have unrelated tests fail on 429s that
        // have nothing to do with what they're actually asserting.
        Environment.SetEnvironmentVariable("RateLimiting__GlobalPermitLimit", "100000");
        Environment.SetEnvironmentVariable("RateLimiting__AuthPermitLimit", "100000");
        // Real OpenTelemetry instrumentation stays on (it's part of what's being verified as wired
        // correctly), but the console exporter would otherwise dump a span/metric block per HTTP
        // call across hundreds of test requests — pure log noise for this suite.
        Environment.SetEnvironmentVariable("Otel__ConsoleExporterEnabled", "false");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
    }
}
