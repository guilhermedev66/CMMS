using Cmms.BuildingBlocks.Database;
using Cmms.Modules.Assets.Infrastructure;
using Cmms.Modules.Audit.Infrastructure;
using Cmms.Modules.IdentityAccess.Infrastructure;
using Cmms.Modules.MaintenanceRequests.Infrastructure;
using Cmms.Modules.WorkManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace Cmms.IntegrationTests;

/// <summary>
/// One real PostgreSQL instance (Testcontainers), shared across every test class in the
/// "Postgres" collection so the container starts/migrates once per test run rather than once per
/// test class. Runs every module's migrations, exactly like Program.cs's own
/// <c>Database:ApplyMigrations</c> path does at startup.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("cmms")
        .WithUsername("cmms")
        .WithPassword("cmms-test-password")
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        await using (var identityDb = CreateContext<IdentityAccessDbContext>(
            options => new IdentityAccessDbContext(options), DatabaseSchemas.IdentityAccess))
        {
            await identityDb.Database.MigrateAsync();
        }

        await using (var assetsDb = CreateContext<AssetsDbContext>(
            options => new AssetsDbContext(options), DatabaseSchemas.Assets))
        {
            await assetsDb.Database.MigrateAsync();
        }

        await using (var auditDb = CreateContext<AuditDbContext>(
            options => new AuditDbContext(options), DatabaseSchemas.Audit))
        {
            await auditDb.Database.MigrateAsync();
        }

        await using (var requestsDb = CreateContext<MaintenanceRequestsDbContext>(
            options => new MaintenanceRequestsDbContext(options), DatabaseSchemas.MaintenanceRequests))
        {
            await requestsDb.Database.MigrateAsync();
        }

        await using (var workOrdersDb = CreateContext<WorkManagementDbContext>(
            options => new WorkManagementDbContext(options), DatabaseSchemas.WorkManagement))
        {
            await workOrdersDb.Database.MigrateAsync();
        }
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    private TContext CreateContext<TContext>(
        Func<DbContextOptions<TContext>, TContext> factory,
        string migrationsSchema)
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(
                ConnectionString,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", migrationsSchema))
            .UseSnakeCaseNamingConvention()
            .Options;

        return factory(options);
    }
}

[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
