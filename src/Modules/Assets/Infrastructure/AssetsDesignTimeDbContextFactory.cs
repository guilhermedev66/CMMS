using Cmms.BuildingBlocks.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cmms.Modules.Assets.Infrastructure;

public sealed class AssetsDesignTimeDbContextFactory : IDesignTimeDbContextFactory<AssetsDbContext>
{
    public AssetsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Cmms")
            ?? "Host=localhost;Port=5432;Database=cmms;Username=cmms;Password=cmms";

        var options = new DbContextOptionsBuilder<AssetsDbContext>()
            .UseNpgsql(
                connectionString,
                postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", DatabaseSchemas.Assets))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AssetsDbContext(options);
    }
}
