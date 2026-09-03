namespace Cmms.Modules.Attachments.Domain;

public sealed class InvalidAttachmentUploadIntentOperationException(string message) : InvalidOperationException(message);

/// <summary>
/// Per docs/02-security-and-invariants.md's attachment pipeline, step 1: "API creates an
/// AttachmentUploadIntent row — bound to actor_id, site_id, parent_resource_type/
/// parent_resource_id, a server-generated random quarantine key, max_bytes, the declared allowed
/// type, an expiry (15 minutes), and state Pending."
///
/// SUBSTITUTION, documented per this codebase's "document trade-offs, don't hide them" convention:
/// docs/02 describes a presigned-PUT-to-S3-compatible-storage flow ("storage bypasses the API
/// server entirely for the byte stream"). This dev/CI environment has no object storage
/// credentials configured (Cloudflare R2 per docs/03 is a deploy-time decision, not something an
/// agent can provision without the account owner's secrets), so bytes instead flow through this
/// API server to <see cref="Infrastructure.IAttachmentStorage"/> (local disk in this environment).
/// Every security property docs/02 cares about is preserved: the quarantine key is still
/// server-generated and never derived from client input, the client never gets write access to the
/// clean key, and finalize still re-authorizes + re-encodes exactly as specified — only the literal
/// transport mechanism (presigned PUT vs. an authenticated POST to this same server) differs.
/// Swapping <see cref="Infrastructure.IAttachmentStorage"/> for a real S3/R2-backed implementation
/// (and this endpoint for a presigned-URL issuer) at deploy time is a drop-in change behind that
/// interface, not a redesign.
/// </summary>
public sealed class AttachmentUploadIntent
{
    private AttachmentUploadIntent()
    {
    }

    public AttachmentUploadIntent(
        Guid actorUserId,
        Guid siteId,
        AttachmentParentResourceType parentResourceType,
        Guid parentResourceId,
        string declaredContentType,
        long maxBytes,
        string? originalFileNameForDisplay)
    {
        Id = Guid.CreateVersion7();
        ActorUserId = actorUserId;
        SiteId = siteId;
        ParentResourceType = parentResourceType;
        ParentResourceId = parentResourceId;
        // Server-generated, opaque, never derived from client input (docs/02) — this is what
        // makes the quarantine key safe to use as a storage path with no further sanitization.
        QuarantineKey = $"quarantine/{Guid.CreateVersion7():N}";
        DeclaredContentType = declaredContentType;
        MaxBytes = maxBytes;
        // Display-only — never used to construct a storage path (see IAttachmentStorage). Trimmed
        // and length-capped defensively; the actual path-traversal defense is that this value is
        // never I/O input, not that it's been sanitized to look safe.
        OriginalFileNameForDisplay = string.IsNullOrWhiteSpace(originalFileNameForDisplay)
            ? null
            : originalFileNameForDisplay.Trim()[..Math.Min(originalFileNameForDisplay.Trim().Length, 255)];
        Status = AttachmentUploadIntentStatus.Pending;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        ExpiresAtUtc = CreatedAtUtc.AddMinutes(15);
    }

    public Guid Id { get; private set; }

    public Guid ActorUserId { get; private set; }

    public Guid SiteId { get; private set; }

    public AttachmentParentResourceType ParentResourceType { get; private set; }

    public Guid ParentResourceId { get; private set; }

    public string QuarantineKey { get; private set; } = string.Empty;

    public string DeclaredContentType { get; private set; } = string.Empty;

    public long MaxBytes { get; private set; }

    public string? OriginalFileNameForDisplay { get; private set; }

    public AttachmentUploadIntentStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public long? UploadedByteCount { get; private set; }

    public bool IsExpired(DateTimeOffset asOfUtc) => asOfUtc > ExpiresAtUtc;

    public void MarkUploaded(long byteCount)
    {
        RequireStatus(AttachmentUploadIntentStatus.Pending, nameof(MarkUploaded));
        UploadedByteCount = byteCount;
        Status = AttachmentUploadIntentStatus.Uploaded;
    }

    public void MarkActive()
    {
        RequireStatus(AttachmentUploadIntentStatus.Uploaded, nameof(MarkActive));
        Status = AttachmentUploadIntentStatus.Active;
    }

    public void Reject()
    {
        if (Status is AttachmentUploadIntentStatus.Active or AttachmentUploadIntentStatus.Rejected)
        {
            throw new InvalidAttachmentUploadIntentOperationException($"Cannot reject an intent in status {Status}.");
        }

        Status = AttachmentUploadIntentStatus.Rejected;
    }

    private void RequireStatus(AttachmentUploadIntentStatus required, string operation)
    {
        if (Status != required)
        {
            throw new InvalidAttachmentUploadIntentOperationException(
                $"{operation} requires status {required}, but the intent is {Status}.");
        }
    }
}
