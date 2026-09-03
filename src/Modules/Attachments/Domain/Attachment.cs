namespace Cmms.Modules.Attachments.Domain;

/// <summary>
/// A finalized, re-encoded attachment. Per docs/02: "Only that clean key can ever become the
/// attachment's Active object — the quarantine key is deleted after finalize ... and never has a
/// download route." This type only ever represents the clean, server-generated result — there is
/// no constructor path that lets a quarantine key become an Attachment's storage key.
/// </summary>
public sealed class Attachment
{
    private Attachment()
    {
    }

    public Attachment(
        Guid uploadIntentId,
        Guid siteId,
        AttachmentParentResourceType parentResourceType,
        Guid parentResourceId,
        string cleanStorageKey,
        string contentType,
        long byteSize,
        int pixelWidth,
        int pixelHeight,
        Guid uploadedByUserId)
    {
        Id = Guid.CreateVersion7();
        UploadIntentId = uploadIntentId;
        SiteId = siteId;
        ParentResourceType = parentResourceType;
        ParentResourceId = parentResourceId;
        CleanStorageKey = cleanStorageKey;
        ContentType = contentType;
        ByteSize = byteSize;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        UploadedByUserId = uploadedByUserId;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }

    public Guid UploadIntentId { get; private set; }

    public Guid SiteId { get; private set; }

    public AttachmentParentResourceType ParentResourceType { get; private set; }

    public Guid ParentResourceId { get; private set; }

    public string CleanStorageKey { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public long ByteSize { get; private set; }

    public int PixelWidth { get; private set; }

    public int PixelHeight { get; private set; }

    public Guid UploadedByUserId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset? UnlinkedAtUtc { get; private set; }

    /// <summary>Soft-unlink only — the row (and its audit trail) is never deleted, matching this
    /// codebase's general append-only-history convention.</summary>
    public void Unlink()
    {
        UnlinkedAtUtc ??= DateTimeOffset.UtcNow;
    }
}
