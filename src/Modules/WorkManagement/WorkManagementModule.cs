using Cmms.BuildingBlocks.Database;
using Cmms.Modules.WorkManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cmms.Modules.WorkManagement;

public static class WorkManagementModule
{
    public static IServiceCollection AddWorkManagement(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Cmms")
            ?? throw new InvalidOperationException("Connection string 'Cmms' is not configured.");

        services.AddDbContext<WorkManagementDbContext>(options =>
            options
                .UseNpgsql(
                    connectionString,
                    postgres => postgres.MigrationsHistoryTable(
                        "__ef_migrations_history",
                        DatabaseSchemas.WorkManagement))
                .UseSnakeCaseNamingConvention());

        return services;
    }
}
