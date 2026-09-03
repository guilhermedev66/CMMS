namespace Cmms.Modules.Assets.Domain;

public sealed class Asset
{
    private Asset()
    {
    }

    public Asset(
        Guid siteId,
        string tag,
        string name,
        string category,
        AssetCriticality criticality,
        Guid? currentLocationId = null,
        Guid? parentAssetId = null)
    {
        Id = Guid.CreateVersion7();
        SiteId = siteId;
        Tag = tag.Trim();
        NormalizedTag = tag.Trim().ToUpperInvariant();
        Name = name.Trim();
        Category = category.Trim();
        Criticality = criticality;
        Status = AssetStatus.InService;
        CurrentLocationId = currentLocationId;
        ParentAssetId = parentAssetId;
        QrLocator = Guid.CreateVersion7();
        CreatedAtUtc = DateTimeOffset.UtcNow;
        RowVersion = 1;
    }

    public Guid Id { get; private set; }

    public Guid SiteId { get; private set; }

    public string Tag { get; private set; } = string.Empty;

    public string NormalizedTag { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string Category { get; private set; } = string.Empty;

    public string? Manufacturer { get; private set; }

    public string? Model { get; private set; }

    public string? SerialNumber { get; private set; }

    public AssetCriticality Criticality { get; private set; }

    public AssetStatus Status { get; private set; }

    public Guid? CurrentLocationId { get; private set; }

    public Guid? ParentAssetId { get; private set; }

    public Guid QrLocator { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public long RowVersion { get; private set; }
}
