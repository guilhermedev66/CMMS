using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace Cmms.Modules.Attachments.Infrastructure;

/// <summary>
/// Dev/CI stand-in for the real S3/R2-backed implementation (see
/// <see cref="Domain.AttachmentUploadIntent"/>'s doc comment). Every key this module ever generates
/// matches <see cref="KeyPattern"/> (<c>quarantine/&lt;32 hex&gt;</c> or <c>clean/&lt;32 hex&gt;</c>)
/// — <see cref="Validate"/> is enforced on every call as defense-in-depth against path traversal,
/// even though no caller in this codebase ever constructs a key from client input in the first
/// place. This is what makes the M4 DoD's "attachment upload rejects path traversal" claim true at
/// the storage layer itself, not only by convention at the call sites above it.
/// </summary>
public sealed partial class LocalDiskAttachmentStorage : IAttachmentStorage
{
    private readonly string _rootDirectory;

    public LocalDiskAttachmentStorage(IConfiguration configuration)
    {
        _rootDirectory = configuration["Attachments:LocalStorageRoot"] ?? "/tmp/cmms-attachments";
        Directory.CreateDirectory(_rootDirectory);
    }

    public async Task WriteAsync(string key, Stream content, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var fileStream = File.Create(path);
        await content.CopyToAsync(fileStream, cancellationToken);
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(key);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Attachment content not found.", key);
        }

        return Task.FromResult<Stream>(File.OpenRead(path));
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string ResolvePath(string key)
    {
        if (!KeyRegex().IsMatch(key))
        {
            throw new ArgumentException(
                $"Rejected as an invalid storage key: \"{key}\". Every key this module writes matches {KeyPattern} — " +
                "anything else (path separators, \"..\", client-supplied text) is refused before it ever reaches the filesystem.",
                nameof(key));
        }

        // Safe by construction at this point (the regex above already rules out ".."/"/" beyond the
        // one literal separator it allows), but resolve-and-recheck containment anyway — a second,
        // independent layer rather than trusting the regex alone.
        var fullPath = Path.GetFullPath(Path.Combine(_rootDirectory, key));
        var fullRoot = Path.GetFullPath(_rootDirectory);
        if (!fullPath.StartsWith(fullRoot, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Resolved storage path escaped the storage root for key \"{key}\".", nameof(key));
        }

        return fullPath;
    }

    private const string KeyPattern = "^(quarantine|clean)/[0-9a-f]{32}$";

    [GeneratedRegex(KeyPattern)]
    private static partial Regex KeyRegex();
}
