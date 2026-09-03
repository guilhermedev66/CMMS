namespace Cmms.Modules.IdentityAccess.Domain;

public enum RoleCode
{
    Admin,
    Planner,
    Technician,
    Requester
}

public enum RoleScope
{
    Company,
    Site
}

public enum PermissionScope
{
    CompanyGlobal,
    AllSites,
    MemberSite,
    OwnRecord,
    OwnAssignment,
    ParentResource
}

public sealed class RoleDefinition
{
    private RoleDefinition()
    {
    }

    internal RoleDefinition(RoleCode code, RoleScope scope)
    {
        Code = code;
        Scope = scope;
    }

    public RoleCode Code { get; private set; }

    public RoleScope Scope { get; private set; }
}

public sealed class PermissionDefinition
{
    private PermissionDefinition()
    {
    }

    internal PermissionDefinition(string code)
    {
        Code = code;
    }

    public string Code { get; private set; } = string.Empty;
}

public sealed class RolePermission
{
    private RolePermission()
    {
    }

    internal RolePermission(
        RoleCode roleCode,
        string permissionCode,
        PermissionScope scope,
        string? resourcePredicate = null)
    {
        RoleCode = roleCode;
        PermissionCode = permissionCode;
        Scope = scope;
        ResourcePredicate = resourcePredicate;
    }

    public RoleCode RoleCode { get; private set; }

    public string PermissionCode { get; private set; } = string.Empty;

    public PermissionScope Scope { get; private set; }

    public string? ResourcePredicate { get; private set; }
}

public sealed class CompanyRoleAssignment
{
    private CompanyRoleAssignment()
    {
    }

    public CompanyRoleAssignment(Guid userId)
    {
        UserId = userId;
        RoleCode = RoleCode.Admin;
        AssignedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid UserId { get; private set; }

    public RoleCode RoleCode { get; private set; }

    public DateTimeOffset AssignedAtUtc { get; private set; }
}

public sealed class SiteMembership
{
    private SiteMembership()
    {
    }

    public SiteMembership(Guid userId, Guid siteId, RoleCode roleCode)
    {
        if (roleCode == RoleCode.Admin)
        {
            throw new ArgumentException("Admin is company-wide and cannot be assigned through a site membership.", nameof(roleCode));
        }

        UserId = userId;
        SiteId = siteId;
        RoleCode = roleCode;
        IsActive = true;
        AssignedAtUtc = DateTimeOffset.UtcNow;
        RowVersion = 1;
    }

    public Guid UserId { get; private set; }

    public Guid SiteId { get; private set; }

    public RoleCode RoleCode { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset AssignedAtUtc { get; private set; }

    public long RowVersion { get; private set; }
}
