using Cmms.BuildingBlocks.Database;
using Cmms.Modules.MaintenanceRequests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cmms.Modules.MaintenanceRequests;

public static class MaintenanceRequestsModule
{
    public static IServiceCollection AddMaintenanceRequests(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Cmms")
            ?? throw new InvalidOperationException("Connection string 'Cmms' is not configured.");

        services.AddDbContext<MaintenanceRequestsDbContext>(options =>
            options
                .UseNpgsql(
                    connectionString,
                    postgres => postgres.MigrationsHistoryTable(
                        "__ef_migrations_history",
                        DatabaseSchemas.MaintenanceRequests))
                .UseSnakeCaseNamingConvention());

        return services;
    }
}
