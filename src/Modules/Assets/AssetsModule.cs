using Cmms.BuildingBlocks.Database;
using Cmms.Modules.Assets.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cmms.Modules.Assets;

public static class AssetsModule
{
    public static IServiceCollection AddAssets(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Cmms")
            ?? throw new InvalidOperationException("Connection string 'Cmms' is not configured.");

        services.AddDbContext<AssetsDbContext>(options =>
            options
                .UseNpgsql(
                    connectionString,
                    postgres => postgres.MigrationsHistoryTable(
                        "__ef_migrations_history",
                        DatabaseSchemas.Assets))
                .UseSnakeCaseNamingConvention());

        return services;
    }
}
