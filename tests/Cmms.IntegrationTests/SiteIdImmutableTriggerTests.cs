using Cmms.BuildingBlocks.Database;
using Cmms.Modules.Assets.Domain;
using Cmms.Modules.Assets.Infrastructure;
using Cmms.Modules.IdentityAccess.Domain;
using Cmms.Modules.IdentityAccess.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace Cmms.IntegrationTests;

/// <summary>
/// Proves the `assets_site_id_immutable` trigger from the Assets module's initial migration
/// (src/Modules/Assets/Infrastructure/Migrations/20260903072718_InitialAssets.cs) actually fires
/// at the database level, per docs/01-domain-and-workflows.md § Site-boundness: "site_id is set
/// once at creation and never changes." This issues a raw UPDATE over the wire — bypassing EF and
/// the application layer entirely — so a passing test can only mean the database itself is
/// enforcing the invariant, not that the C# layer merely declines to expose a way to do it.
/// </summary>
[Collection("Postgres")]
public sealed class SiteIdImmutableTriggerTests
{
    private readonly PostgresFixture _postgres;

    public SiteIdImmutableTriggerTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    [Fact]
    public async Task Raw_update_of_an_assets_site_id_is_rejected_by_the_database_trigger()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var identityOptions = new DbContextOptionsBuilder<IdentityAccessDbContext>()
            .UseNpgsql(_postgres.ConnectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", DatabaseSchemas.IdentityAccess))
            .UseSnakeCaseNamingConvention()
            .Options;

        Guid siteAId, siteBId;
        await using (var identityDb = new IdentityAccessDbContext(identityOptions))
        {
            var siteA = new Site($"TRIG-A-{suffix}", "Trigger Site A", "UTC");
            var siteB = new Site($"TRIG-B-{suffix}", "Trigger Site B", "UTC");
            identityDb.Sites.AddRange(siteA, siteB);
            await identityDb.SaveChangesAsync();
            siteAId = siteA.Id;
            siteBId = siteB.Id;
        }

        var assetsOptions = new DbContextOptionsBuilder<AssetsDbContext>()
            .UseNpgsql(_postgres.ConnectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", DatabaseSchemas.Assets))
            .UseSnakeCaseNamingConvention()
            .Options;

        Guid assetId;
        await using (var assetsDb = new AssetsDbContext(assetsOptions))
        {
            var asset = new Asset(siteAId, $"TRIG-{suffix}", "Trigger Test Asset", "General", AssetCriticality.C);
            assetsDb.Assets.Add(asset);
            await assetsDb.SaveChangesAsync();
            assetId = asset.Id;
        }

        await using var connection = new NpgsqlConnection(_postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE assets.assets SET site_id = @newSiteId WHERE id = @id";
        command.Parameters.AddWithValue("newSiteId", siteBId);
        command.Parameters.AddWithValue("id", assetId);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal("23514", exception.SqlState); // matches the ERRCODE the trigger raises with.
        Assert.Contains("site_id is immutable", exception.MessageText, StringComparison.OrdinalIgnoreCase);

        await using var verifyDb = new AssetsDbContext(assetsOptions);
        var reloaded = await verifyDb.Assets.AsNoTracking().SingleAsync(a => a.Id == assetId);
        Assert.Equal(siteAId, reloaded.SiteId);
    }
}
