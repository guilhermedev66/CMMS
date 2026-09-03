using Cmms.BuildingBlocks.Database;
using Cmms.Modules.IdentityAccess.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Cmms.Modules.IdentityAccess.Infrastructure;

public sealed class IdentityAccessDbContext(DbContextOptions<IdentityAccessDbContext> options)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
    public DbSet<Site> Sites => Set<Site>();

    public DbSet<SiteMembership> SiteMemberships => Set<SiteMembership>();

    public DbSet<RoleDefinition> RoleDefinitions => Set<RoleDefinition>();

    public DbSet<PermissionDefinition> PermissionDefinitions => Set<PermissionDefinition>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<CompanyRoleAssignment> CompanyRoleAssignments => Set<CompanyRoleAssignment>();

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
        base.OnModelCreating(builder);

        builder.HasDefaultSchema(DatabaseSchemas.IdentityAccess);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("users");
            entity.Property(user => user.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(user => user.UserName).IsRequired();
            entity.Property(user => user.NormalizedUserName).IsRequired();
            entity.Property(user => user.Email).IsRequired();
            entity.Property(user => user.NormalizedEmail).IsRequired();
            entity.Property(user => user.CreatedAtUtc).HasColumnType("timestamp with time zone");
            entity.HasIndex(user => user.NormalizedEmail).IsUnique();
        });

        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");

        ConfigureSites(builder);
        ConfigureAccessModel(builder);
    }

    private static void ConfigureSites(ModelBuilder builder)
    {
        builder.Entity<Site>(entity =>
        {
            entity.ToTable("sites");
            entity.HasKey(site => site.Id);
            entity.Property(site => site.Code).HasMaxLength(50).IsRequired();
            entity.Property(site => site.Name).HasMaxLength(200).IsRequired();
            entity.Property(site => site.TimeZone).HasMaxLength(100).IsRequired();
            entity.Property(site => site.CreatedAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(site => site.RowVersion).IsConcurrencyToken().HasDefaultValue(1L);
            entity.HasIndex(site => site.Code).IsUnique();
        });
    }

    private static void ConfigureAccessModel(ModelBuilder builder)
    {
        builder.Entity<RoleDefinition>(entity =>
        {
            entity.ToTable("role_definitions");
            entity.HasKey(role => role.Code);
            entity.Property(role => role.Code).HasConversion<string>().HasMaxLength(30);
            entity.Property(role => role.Scope).HasConversion<string>().HasMaxLength(20);
            entity.HasData(
                new RoleDefinition(RoleCode.Admin, RoleScope.Company),
                new RoleDefinition(RoleCode.Planner, RoleScope.Site),
                new RoleDefinition(RoleCode.Technician, RoleScope.Site),
                new RoleDefinition(RoleCode.Requester, RoleScope.Site));
        });

        builder.Entity<PermissionDefinition>(entity =>
        {
            entity.ToTable("permission_definitions");
            entity.HasKey(permission => permission.Code);
            entity.Property(permission => permission.Code).HasMaxLength(100);
            entity.HasData(PermissionCatalog.All.Select(code => new PermissionDefinition(code)));
        });

        builder.Entity<RolePermission>(entity =>
        {
            entity.ToTable("role_permissions");
            entity.HasKey(grant => new { grant.RoleCode, grant.PermissionCode });
            entity.Property(grant => grant.RoleCode).HasConversion<string>().HasMaxLength(30);
            entity.Property(grant => grant.PermissionCode).HasMaxLength(100);
            entity.Property(grant => grant.Scope).HasConversion<string>().HasMaxLength(30);
            entity.Property(grant => grant.ResourcePredicate).HasMaxLength(100);
            entity.HasOne<RoleDefinition>()
                .WithMany()
                .HasForeignKey(grant => grant.RoleCode)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PermissionDefinition>()
                .WithMany()
                .HasForeignKey(grant => grant.PermissionCode)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasData(RolePermissionSeed.All);
        });

        builder.Entity<CompanyRoleAssignment>(entity =>
        {
            entity.ToTable(
                "company_role_assignments",
                table => table.HasCheckConstraint(
                    "ck_company_role_assignments_admin_only",
                    "role_code = 'Admin'"));
            entity.HasKey(assignment => new { assignment.UserId, assignment.RoleCode });
            entity.Property(assignment => assignment.RoleCode).HasConversion<string>().HasMaxLength(30);
            entity.Property(assignment => assignment.AssignedAtUtc).HasColumnType("timestamp with time zone");
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(assignment => assignment.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RoleDefinition>()
                .WithMany()
                .HasForeignKey(assignment => assignment.RoleCode)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<SiteMembership>(entity =>
        {
            entity.ToTable(
                "site_memberships",
                table => table.HasCheckConstraint(
                    "ck_site_memberships_site_roles_only",
                    "role_code IN ('Planner', 'Technician', 'Requester')"));
            entity.HasKey(membership => new { membership.UserId, membership.SiteId });
            entity.Property(membership => membership.RoleCode).HasConversion<string>().HasMaxLength(30);
            entity.Property(membership => membership.AssignedAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(membership => membership.RowVersion).IsConcurrencyToken().HasDefaultValue(1L);
            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(membership => membership.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Site>()
                .WithMany()
                .HasForeignKey(membership => membership.SiteId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<RoleDefinition>()
                .WithMany()
                .HasForeignKey(membership => membership.RoleCode)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(membership => new { membership.SiteId, membership.RoleCode, membership.IsActive });
        });
    }

    private void IncrementRowVersions()
    {
        foreach (var entry in ChangeTracker.Entries()
                     .Where(entry =>
                         entry.State == EntityState.Modified &&
                         entry.Entity is Site or SiteMembership))
        {
            var property = entry.Property(nameof(Site.RowVersion));
            property.CurrentValue = (long)property.OriginalValue! + 1;
        }
    }
}
