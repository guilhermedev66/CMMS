using Cmms.BuildingBlocks.Database;
using Cmms.Modules.PreventiveMaintenance.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cmms.Modules.PreventiveMaintenance;

/// <summary>
/// Only this module's own DbContext registration — the occurrence-generation orchestration
/// (<c>Cmms.Api.MaintenancePlanGenerationRunner</c>) lives in Cmms.Api instead, alongside
/// MaintenanceRequestsEndpoints' Convert-to-Work-Order flow, because both cross this module's
/// schema boundary into WorkManagement + Audit — this module itself never references another
/// module's project, same as every other module in this codebase.
/// </summary>
public static class PreventiveMaintenanceModule
{
    public static IServiceCollection AddPreventiveMaintenance(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Cmms")
            ?? throw new InvalidOperationException("Connection string 'Cmms' is not configured.");

        services.AddDbContext<PreventiveMaintenanceDbContext>(options =>
            options
                .UseNpgsql(
                    connectionString,
                    postgres => postgres.MigrationsHistoryTable(
                        "__ef_migrations_history",
                        DatabaseSchemas.PreventiveMaintenance))
                .UseSnakeCaseNamingConvention());

        return services;
    }
}
