using Cmms.BuildingBlocks.Database;
using Cmms.Modules.Attachments.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cmms.Modules.Attachments;

public static class AttachmentsModule
{
    public static IServiceCollection AddAttachments(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Cmms")
            ?? throw new InvalidOperationException("Connection string 'Cmms' is not configured.");

        services.AddDbContext<AttachmentsDbContext>(options =>
            options
                .UseNpgsql(
                    connectionString,
                    postgres => postgres.MigrationsHistoryTable(
                        "__ef_migrations_history",
                        DatabaseSchemas.Attachments))
                .UseSnakeCaseNamingConvention());

        services.AddSingleton<IAttachmentStorage, LocalDiskAttachmentStorage>();

        return services;
    }
}
