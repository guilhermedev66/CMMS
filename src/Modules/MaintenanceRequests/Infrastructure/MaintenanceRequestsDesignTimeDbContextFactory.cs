using Cmms.BuildingBlocks.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cmms.Modules.MaintenanceRequests.Infrastructure;

public sealed class MaintenanceRequestsDesignTimeDbContextFactory : IDesignTimeDbContextFactory<MaintenanceRequestsDbContext>
{
    public MaintenanceRequestsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Cmms")
            ?? "Host=localhost;Port=5432;Database=cmms;Username=cmms;Password=cmms";

        var options = new DbContextOptionsBuilder<MaintenanceRequestsDbContext>()
            .UseNpgsql(
                connectionString,
                postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", DatabaseSchemas.MaintenanceRequests))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new MaintenanceRequestsDbContext(options);
    }
}
