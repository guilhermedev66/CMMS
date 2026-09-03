using Cmms.BuildingBlocks.Database;
using Cmms.Modules.Assets.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cmms.Modules.Assets.Infrastructure;

public sealed class AssetsDbContext(DbContextOptions<AssetsDbContext> options) : DbContext(options)
{
    public DbSet<Asset> Assets => Set<Asset>();

    public DbSet<Location> Locations => Set<Location>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        IncrementRowVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        IncrementRowVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(DatabaseSchemas.Assets);

        builder.Entity<Location>(entity =>
        {
            entity.ToTable(
                "locations",
                table =>
                {
                    table.HasCheckConstraint("ck_locations_not_own_parent", "parent_location_id IS NULL OR parent_location_id <> id");
                    table.HasCheckConstraint("ck_locations_code_normalized", "code = upper(btrim(code))");
                });
            entity.HasKey(location => location.Id);
            entity.HasAlternateKey(location => new { location.SiteId, location.Id });
            entity.Property(location => location.Code).HasMaxLength(50).IsRequired();
            entity.Property(location => location.Name).HasMaxLength(200).IsRequired();
            entity.Property(location => location.Description).HasMaxLength(1000);
            entity.Property(location => location.CreatedAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(location => location.RowVersion).IsConcurrencyToken().HasDefaultValue(1L);
            entity.HasIndex(location => new { location.SiteId, location.Code }).IsUnique();
            entity.HasOne<Location>()
                .WithMany()
                .HasForeignKey(location => new { location.SiteId, location.ParentLocationId })
                .HasPrincipalKey(location => new { location.SiteId, location.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Asset>(entity =>
        {
            entity.ToTable(
                "assets",
                table =>
                {
                    table.HasCheckConstraint("ck_assets_not_own_parent", "parent_asset_id IS NULL OR parent_asset_id <> id");
                    table.HasCheckConstraint("ck_assets_normalized_tag", "normalized_tag = upper(btrim(tag))");
                    table.HasCheckConstraint("ck_assets_criticality", "criticality IN ('A', 'B', 'C')");
                    table.HasCheckConstraint("ck_assets_status", "status IN ('InService', 'OutOfService', 'Retired')");
                });
            entity.HasKey(asset => asset.Id);
            entity.HasAlternateKey(asset => new { asset.SiteId, asset.Id });
            entity.Property(asset => asset.Tag).HasMaxLength(100).IsRequired();
            entity.Property(asset => asset.NormalizedTag).HasMaxLength(100).IsRequired();
            entity.Property(asset => asset.Name).HasMaxLength(200).IsRequired();
            entity.Property(asset => asset.Category).HasMaxLength(100).IsRequired();
            entity.Property(asset => asset.Manufacturer).HasMaxLength(200);
            entity.Property(asset => asset.Model).HasMaxLength(200);
            entity.Property(asset => asset.SerialNumber).HasMaxLength(200);
            entity.Property(asset => asset.Criticality).HasConversion<string>().HasMaxLength(1);
            entity.Property(asset => asset.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(asset => asset.CreatedAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(asset => asset.RowVersion).IsConcurrencyToken().HasDefaultValue(1L);
            entity.HasIndex(asset => asset.NormalizedTag).IsUnique();
            entity.HasIndex(asset => asset.QrLocator).IsUnique();
            entity.HasIndex(asset => new { asset.SiteId, asset.CurrentLocationId });
            entity.HasOne<Location>()
                .WithMany()
                .HasForeignKey(asset => new { asset.SiteId, asset.CurrentLocationId })
                .HasPrincipalKey(location => new { location.SiteId, location.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Asset>()
                .WithMany()
                .HasForeignKey(asset => new { asset.SiteId, asset.ParentAssetId })
                .HasPrincipalKey(asset => new { asset.SiteId, asset.Id })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private void IncrementRowVersions()
    {
        foreach (var entry in ChangeTracker.Entries()
                     .Where(entry =>
                         entry.State == EntityState.Modified &&
                         entry.Entity is Asset or Location))
        {
            var property = entry.Property(nameof(Asset.RowVersion));
            property.CurrentValue = (long)property.OriginalValue! + 1;
        }
    }
}
