using Cmms.BuildingBlocks.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cmms.Modules.WorkManagement.Infrastructure;

public sealed class WorkManagementDesignTimeDbContextFactory : IDesignTimeDbContextFactory<WorkManagementDbContext>
{
    public WorkManagementDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Cmms")
            ?? "Host=localhost;Port=5432;Database=cmms;Username=cmms;Password=cmms";

        var options = new DbContextOptionsBuilder<WorkManagementDbContext>()
            .UseNpgsql(
                connectionString,
                postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", DatabaseSchemas.WorkManagement))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new WorkManagementDbContext(options);
    }
}
