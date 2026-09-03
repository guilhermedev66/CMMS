using Cmms.BuildingBlocks.Database;
using Cmms.Modules.Audit.Application;
using Cmms.Modules.Audit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cmms.Modules.Audit;

public static class AuditModule
{
    public static IServiceCollection AddAudit(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Cmms")
            ?? throw new InvalidOperationException("Connection string 'Cmms' is not configured.");

        services.AddDbContext<AuditDbContext>(options =>
            options
                .UseNpgsql(
                    connectionString,
                    postgres => postgres.MigrationsHistoryTable(
                        "__ef_migrations_history",
                        DatabaseSchemas.Audit))
                .UseSnakeCaseNamingConvention());

        services.AddScoped<IAuditEventWriter, AuditEventWriter>();

        return services;
    }
}
