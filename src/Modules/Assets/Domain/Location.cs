namespace Cmms.Modules.Assets.Domain;

public sealed class Location
{
    private Location()
    {
    }

    public Location(Guid siteId, string code, string name, Guid? parentLocationId = null)
    {
        Id = Guid.CreateVersion7();
        SiteId = siteId;
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        ParentLocationId = parentLocationId;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        RowVersion = 1;
    }

    public Guid Id { get; private set; }

    public Guid SiteId { get; private set; }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public Guid? ParentLocationId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public long RowVersion { get; private set; }
}
