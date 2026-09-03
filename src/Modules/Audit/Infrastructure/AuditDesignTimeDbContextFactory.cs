using Cmms.BuildingBlocks.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cmms.Modules.Audit.Infrastructure;

public sealed class AuditDesignTimeDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Cmms")
            ?? "Host=localhost;Port=5432;Database=cmms;Username=cmms;Password=cmms";

        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(
                connectionString,
                postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", DatabaseSchemas.Audit))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AuditDbContext(options);
    }
}
