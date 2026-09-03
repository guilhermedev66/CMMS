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
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
    }
}
