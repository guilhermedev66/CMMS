using Cmms.BuildingBlocks.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cmms.Modules.Attachments.Infrastructure;

public sealed class AttachmentsDesignTimeDbContextFactory : IDesignTimeDbContextFactory<AttachmentsDbContext>
{
    public AttachmentsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Cmms")
            ?? "Host=localhost;Port=5432;Database=cmms;Username=cmms;Password=cmms";

        var options = new DbContextOptionsBuilder<AttachmentsDbContext>()
            .UseNpgsql(
                connectionString,
                postgres => postgres.MigrationsHistoryTable("__ef_migrations_history", DatabaseSchemas.Attachments))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new AttachmentsDbContext(options);
    }
}
