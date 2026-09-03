using Cmms.BuildingBlocks.Database;
using Cmms.Modules.Assets.Domain;
using Cmms.Modules.Assets.Infrastructure;
using Cmms.Modules.Audit.Domain;
using Cmms.Modules.Audit.Infrastructure;
using Cmms.Modules.IdentityAccess.Domain;
using Cmms.Modules.IdentityAccess.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Cmms.IntegrationTests;

/// <summary>
/// Proves <see cref="SharedTransactionScope"/> genuinely ties an Assets-module mutation and an
/// Audit-module row together in one physical database transaction, per
/// docs/02-security-and-invariants.md § "Audit trail": "Written in the same transaction as the
/// domain change ... not bolted on afterward by a generic interceptor." Rather than trust that
/// wiring, this rolls the shared transaction back and asserts *both* writes vanished together,
/// then repeats the same sequence and commits to prove the positive path also holds.
/// </summary>
[Collection("Postgres")]
public sealed class AuditTransactionTests
{
    private readonly PostgresFixture _postgres;

    public AuditTransactionTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    [Fact]
    public async Task Asset_mutation_and_its_audit_event_only_survive_together()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var identityOptions = new DbContextOptionsBuilder<IdentityAccessDbContext>()
            .UseNpgsql(_postgres.ConnectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", DatabaseSchemas.IdentityAccess))
            .UseSnakeCaseNamingConvention()
            .Options;

        Guid siteId;
        await using (var identityDb = new IdentityAccessDbContext(identityOptions))
        {
            var site = new Site($"AUDIT-{suffix}", "Audit Test Site", "UTC");
            identityDb.Sites.Add(site);
            await identityDb.SaveChangesAsync();
            siteId = site.Id;
        }

        var assetsOptions = new DbContextOptionsBuilder<AssetsDbContext>()
            .UseNpgsql(_postgres.ConnectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", DatabaseSchemas.Assets))
            .UseSnakeCaseNamingConvention()
            .Options;

        Guid assetId;
        await using (var assetsDb = new AssetsDbContext(assetsOptions))
        {
            var asset = new Asset(siteId, $"AUD-{suffix}", "Audit Test Asset", "General", AssetCriticality.C);
            assetsDb.Assets.Add(asset);
            await assetsDb.SaveChangesAsync();
            assetId = asset.Id;
        }

        // --- Act 1: mutate + audit inside a shared transaction, then explicitly roll back. ---
        await using (var txScope = await SharedTransactionScope.BeginAsync(_postgres.ConnectionString))
        {
            await using var assetsDb = txScope.CreateContext<AssetsDbContext>(options => new AssetsDbContext(options));
            await using var auditDb = txScope.CreateContext<AuditDbContext>(options => new AuditDbContext(options));

            var asset = await assetsDb.Assets.SingleAsync(a => a.Id == assetId);
            asset.ChangeCriticality(AssetCriticality.A);
            await assetsDb.SaveChangesAsync();

            auditDb.AuditEvents.Add(new AuditEvent(
                actorUserId: null,
                action: "asset.criticality.changed",
                resourceType: "Asset",
                resourceId: assetId,
                siteId: siteId,
                correlationId: null,
                reason: "integration test rollback",
                beforeJson: """{"criticality":"C"}""",
                afterJson: """{"criticality":"A"}"""));
            await auditDb.SaveChangesAsync();

            await txScope.RollbackAsync();
        }

        // --- Assert 1: neither write survived the rollback. ---
        await using (var assetsDb = new AssetsDbContext(assetsOptions))
        {
            var reloaded = await assetsDb.Assets.AsNoTracking().SingleAsync(a => a.Id == assetId);
            Assert.Equal(AssetCriticality.C, reloaded.Criticality);
        }

        await using (var auditDb = CreateAuditContext())
        {
            var count = await auditDb.AuditEvents.CountAsync(e => e.ResourceId == assetId);
            Assert.Equal(0, count);
        }

        // --- Act 2: repeat the identical sequence, this time committing. ---
        await using (var txScope = await SharedTransactionScope.BeginAsync(_postgres.ConnectionString))
        {
            await using var assetsDb = txScope.CreateContext<AssetsDbContext>(options => new AssetsDbContext(options));
            await using var auditDb = txScope.CreateContext<AuditDbContext>(options => new AuditDbContext(options));

            var asset = await assetsDb.Assets.SingleAsync(a => a.Id == assetId);
            asset.ChangeCriticality(AssetCriticality.A);
            await assetsDb.SaveChangesAsync();

            auditDb.AuditEvents.Add(new AuditEvent(
                actorUserId: null,
                action: "asset.criticality.changed",
                resourceType: "Asset",
                resourceId: assetId,
                siteId: siteId,
                correlationId: null,
                reason: "integration test commit",
                beforeJson: """{"criticality":"C"}""",
                afterJson: """{"criticality":"A"}"""));
            await auditDb.SaveChangesAsync();

            await txScope.CommitAsync();
        }

        // --- Assert 2: both writes survived the commit, together. ---
        await using (var assetsDb = new AssetsDbContext(assetsOptions))
        {
            var reloaded = await assetsDb.Assets.AsNoTracking().SingleAsync(a => a.Id == assetId);
            Assert.Equal(AssetCriticality.A, reloaded.Criticality);
        }

        await using (var auditDb = CreateAuditContext())
        {
            var events = await auditDb.AuditEvents.AsNoTracking()
                .Where(e => e.ResourceId == assetId)
                .ToListAsync();
            var auditEvent = Assert.Single(events);
            Assert.Equal("asset.criticality.changed", auditEvent.Action);
            Assert.Equal("integration test commit", auditEvent.Reason);
            Assert.Equal(siteId, auditEvent.SiteId);
        }
    }

    private AuditDbContext CreateAuditContext()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql(_postgres.ConnectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", DatabaseSchemas.Audit))
            .UseSnakeCaseNamingConvention()
            .Options;
        return new AuditDbContext(options);
    }
}
