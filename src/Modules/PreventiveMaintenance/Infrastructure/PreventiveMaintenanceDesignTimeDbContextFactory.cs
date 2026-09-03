using Cmms.BuildingBlocks.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cmms.Modules.PreventiveMaintenance.Infrastructure;

public sealed class PreventiveMaintenanceDesignTimeDbContextFactory : IDesignTimeDbContextFactory<PreventiveMaintenanceDbContext>
{
    public PreventiveMaintenanceDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Cmms")
            ?? "Host=localhost;Port=5432;Database=cmms;Username=cmms;Password=cmms";

        var options = new DbContextOptionsBuilder<PreventiveMaintenanceDbContext>()
            .UseNpgsql(
                connectionString,
                postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", DatabaseSchemas.PreventiveMaintenance))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new PreventiveMaintenanceDbContext(options);
    }
}
