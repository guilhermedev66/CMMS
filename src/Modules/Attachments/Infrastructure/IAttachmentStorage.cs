namespace Cmms.Modules.Attachments.Infrastructure;

/// <summary>
/// Byte storage for quarantine and clean attachment keys. Swappable behind this interface for a
/// real S3/R2-backed implementation at deploy time — see
/// <see cref="Domain.AttachmentUploadIntent"/>'s doc comment for why this dev/CI environment uses
/// <see cref="LocalDiskAttachmentStorage"/> instead. Every implementation must uphold the same
/// contract this one does: a "key" is always an opaque, server-generated identifier — callers never
/// pass client-supplied text through to a key.
/// </summary>
public interface IAttachmentStorage
{
    Task WriteAsync(string key, Stream content, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
