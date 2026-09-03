using System.Security.Claims;
using System.Text.Json;
using Cmms.BuildingBlocks.Database;
using Cmms.Modules.Attachments.Domain;
using Cmms.Modules.Attachments.Infrastructure;
using Cmms.Modules.Audit.Application;
using Cmms.Modules.Audit.Infrastructure;
using Cmms.Modules.IdentityAccess.Application;
using Cmms.Modules.IdentityAccess.Domain;
using Cmms.Modules.WorkManagement.Domain;
using Cmms.Modules.WorkManagement.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace Cmms.Api;

/// <summary>
/// Attachment upload/finalize/download/unlink, per docs/02-security-and-invariants.md § "Attachment
/// strategy" — the 5-step quarantine -> re-encode -> clean-key pipeline, with the documented
/// substitution: bytes flow through this API server rather than a presigned S3/R2 PUT (see
/// <see cref="AttachmentUploadIntent"/>'s doc comment for the full rationale). Every security
/// property that substitution doesn't touch is still enforced here: the quarantine/clean keys are
/// always server-generated (<see cref="LocalDiskAttachmentStorage"/>), only a raster image that
/// survives decode+re-encode ever reaches a clean key, and every step re-authorizes the actor
/// against the parent Work Order's *current* state, not the state at intent-creation time.
/// </summary>
internal static class AttachmentsEndpoints
{
    /// <summary>Raster evidence photos only, per docs/02's v1 narrowing — no PDFs/manuals, no SVG
    /// (SVG cannot be decoded by ImageSharp at all, so it is rejected by construction, not by an
    /// explicit denylist check).</summary>
    private static readonly IReadOnlySet<string> AllowedContentTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/webp" };

    private const long HardMaxBytes = 15 * 1024 * 1024;
    private const int MaxPixelDimension = 8000;
    private const long MaxPixelArea = 40_000_000;

    public static IEndpointRouteBuilder MapAttachmentsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var workOrderAttachments = endpoints.MapGroup("/work-orders/{workOrderId:guid}/attachments")
            .WithTags("Attachments").RequireAuthorization();
        workOrderAttachments.MapGet("", ListAttachmentsAsync);
        workOrderAttachments.MapPost("/upload-intents", CreateUploadIntentAsync);

        var attachments = endpoints.MapGroup("/attachments").WithTags("Attachments").RequireAuthorization();
        attachments.MapPut("/upload-intents/{intentId:guid}/bytes", UploadBytesAsync);
        attachments.MapPost("/upload-intents/{intentId:guid}/finalize", FinalizeAsync);
        attachments.MapGet("/{id:guid}/download", DownloadAsync);
        attachments.MapPost("/{id:guid}/unlink", UnlinkAsync);

        return endpoints;
    }

    // ---------- List / create intent ----------

    private static async Task<IResult> ListAttachmentsAsync(
        Guid workOrderId,
        ClaimsPrincipal user,
        WorkManagementDbContext workOrdersDb,
        AttachmentsDbContext attachmentsDb,
        IPermissionEvaluator permissions,
        CancellationToken cancellationToken)
    {
        var workOrder = await workOrdersDb.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workOrderId, cancellationToken);
        if (workOrder is null || !await CanReadWorkOrderAsync(workOrder, user, permissions, cancellationToken))
        {
            return Results.NotFound();
        }

        var attachments = await attachmentsDb.Attachments.AsNoTracking()
            .Where(a => a.ParentResourceType == AttachmentParentResourceType.WorkOrder &&
                        a.ParentResourceId == workOrderId && a.UnlinkedAtUtc == null)
            .OrderBy(a => a.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return Results.Ok(attachments.Select(AttachmentResponse.From));
    }

    private static async Task<IResult> CreateUploadIntentAsync(
        Guid workOrderId,
        CreateUploadIntentRequest request,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        WorkManagementDbContext workOrdersDb,
        AttachmentsDbContext attachmentsDb,
        IPermissionEvaluator permissions,
        CancellationToken cancellationToken)
    {
        if (!await AntiforgeryHelpers.HasValidAntiforgeryTokenAsync(httpContext, antiforgery))
        {
            return Results.BadRequest(new { error = "Invalid anti-forgery token." });
        }

        if (!AllowedContentTypes.Contains(request.DeclaredContentType))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["declaredContentType"] = ["Only image/jpeg, image/png, or image/webp evidence photos are accepted."]
            });
        }

        var workOrder = await workOrdersDb.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workOrderId, cancellationToken);
        var actorUserId = permissions.GetUserId(httpContext.User);
        if (workOrder is null || actorUserId is null || !await CanWriteWorkOrderAsync(workOrder, httpContext.User, permissions, cancellationToken))
        {
            return Results.NotFound();
        }

        if (workOrder.Status is WorkOrderStatus.Closed or WorkOrderStatus.Cancelled)
        {
            return Results.Conflict(new { error = $"This Work Order is {workOrder.Status} and no longer accepts new attachments." });
        }

        var intent = new AttachmentUploadIntent(
            actorUserId.Value, workOrder.SiteId, AttachmentParentResourceType.WorkOrder, workOrderId,
            request.DeclaredContentType, HardMaxBytes, request.OriginalFileName);

        attachmentsDb.UploadIntents.Add(intent);
        await attachmentsDb.SaveChangesAsync(cancellationToken);

        return Results.Created($"/attachments/upload-intents/{intent.Id}", UploadIntentResponse.From(intent));
    }

    // ---------- Upload bytes (substitutes the presigned PUT — see class doc comment) ----------

    private static async Task<IResult> UploadBytesAsync(
        Guid intentId,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        AttachmentsDbContext attachmentsDb,
        IAttachmentStorage storage,
        IPermissionEvaluator permissions,
        CancellationToken cancellationToken)
    {
        if (!await AntiforgeryHelpers.HasValidAntiforgeryTokenAsync(httpContext, antiforgery))
        {
            return Results.BadRequest(new { error = "Invalid anti-forgery token." });
        }

        var intent = await attachmentsDb.UploadIntents.FirstOrDefaultAsync(i => i.Id == intentId, cancellationToken);
        var actorUserId = permissions.GetUserId(httpContext.User);
        if (intent is null || actorUserId != intent.ActorUserId)
        {
            return Results.NotFound();
        }

        if (intent.IsExpired(DateTimeOffset.UtcNow))
        {
            return Results.Problem(statusCode: StatusCodes.Status410Gone, title: "This upload intent has expired.");
        }

        if (intent.Status != AttachmentUploadIntentStatus.Pending)
        {
            return Results.Conflict(new { error = $"This upload intent is {intent.Status}, not Pending." });
        }

        byte[] buffer;
        try
        {
            buffer = await ReadBoundedAsync(httpContext.Request.Body, intent.MaxBytes, cancellationToken);
        }
        catch (BodyTooLargeException)
        {
            return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: $"Upload exceeds the {intent.MaxBytes}-byte limit for this intent.");
        }

        if (buffer.Length == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["body"] = ["No bytes were uploaded."] });
        }

        await storage.WriteAsync(intent.QuarantineKey, new MemoryStream(buffer), cancellationToken);
        intent.MarkUploaded(buffer.Length);
        await attachmentsDb.SaveChangesAsync(cancellationToken);

        return Results.Ok(UploadIntentResponse.From(intent));
    }

    // ---------- Finalize: re-authorize, verify, decode+re-encode, write clean key ----------

    private static async Task<IResult> FinalizeAsync(
        Guid intentId,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        IConfiguration configuration,
        IAttachmentStorage storage,
        IPermissionEvaluator permissions,
        IAuditEventWriter auditWriter,
        CancellationToken cancellationToken)
    {
        if (!await AntiforgeryHelpers.HasValidAntiforgeryTokenAsync(httpContext, antiforgery))
        {
            return Results.BadRequest(new { error = "Invalid anti-forgery token." });
        }

        var connectionString = configuration.GetConnectionString("Cmms")
            ?? throw new InvalidOperationException("Connection string 'Cmms' is not configured.");

        await using var transactionScope = await SharedTransactionScope.BeginAsync(connectionString, cancellationToken);
        await using var attachmentsDb = transactionScope.CreateContext<AttachmentsDbContext>(options => new AttachmentsDbContext(options));
        await using var workOrdersDb = transactionScope.CreateContext<WorkManagementDbContext>(options => new WorkManagementDbContext(options));
        await using var auditDb = transactionScope.CreateContext<AuditDbContext>(options => new AuditDbContext(options));

        var intent = await attachmentsDb.UploadIntents.FirstOrDefaultAsync(i => i.Id == intentId, cancellationToken);
        var actorUserId = permissions.GetUserId(httpContext.User);
        if (intent is null || actorUserId != intent.ActorUserId)
        {
            return Results.NotFound();
        }

        if (intent.IsExpired(DateTimeOffset.UtcNow))
        {
            return Results.Problem(statusCode: StatusCodes.Status410Gone, title: "This upload intent has expired.");
        }

        if (intent.Status != AttachmentUploadIntentStatus.Uploaded)
        {
            return Results.Conflict(new { error = $"This upload intent is {intent.Status}, not Uploaded." });
        }

        // Re-authorize against the parent Work Order's CURRENT state — docs/02: "not just the
        // state at step 1, the parent could have closed/been reassigned meanwhile."
        var workOrder = await workOrdersDb.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => w.Id == intent.ParentResourceId, cancellationToken);
        if (workOrder is null || !await CanWriteWorkOrderAsync(workOrder, httpContext.User, permissions, cancellationToken))
        {
            return Results.NotFound();
        }

        if (workOrder.Status is WorkOrderStatus.Closed or WorkOrderStatus.Cancelled)
        {
            await RejectAsync(intent, attachmentsDb, storage, transactionScope, cancellationToken);
            return Results.Conflict(new { error = $"This Work Order is {workOrder.Status}; the pending upload was rejected." });
        }

        await using var identifyStream = await storage.OpenReadAsync(intent.QuarantineKey, cancellationToken);
        ImageInfo imageInfo;
        try
        {
            imageInfo = await Image.IdentifyAsync(identifyStream, cancellationToken);
        }
        catch (Exception)
        {
            // ImageSharp throws (UnknownImageFormatException, InvalidImageContentException, ...)
            // for anything that isn't a structurally valid image it recognizes — SVG included,
            // since ImageSharp has no SVG decoder at all. That failure itself IS the magic-byte
            // verification docs/02 asks for; there is no separate signature check to bypass.
            await RejectAsync(intent, attachmentsDb, storage, transactionScope, cancellationToken);
            return Results.UnprocessableEntity(new { error = "The uploaded file is not a recognizable JPEG, PNG, or WebP image." });
        }

        var detectedMime = imageInfo.Metadata.DecodedImageFormat?.DefaultMimeType;
        if (detectedMime is null || !AllowedContentTypes.Contains(detectedMime) ||
            !string.Equals(detectedMime, intent.DeclaredContentType, StringComparison.OrdinalIgnoreCase))
        {
            await RejectAsync(intent, attachmentsDb, storage, transactionScope, cancellationToken);
            return Results.UnprocessableEntity(new { error = "The uploaded file's actual format does not match the declared content type." });
        }

        if (imageInfo.Width > MaxPixelDimension || imageInfo.Height > MaxPixelDimension ||
            (long)imageInfo.Width * imageInfo.Height > MaxPixelArea)
        {
            await RejectAsync(intent, attachmentsDb, storage, transactionScope, cancellationToken);
            return Results.UnprocessableEntity(new { error = $"Image dimensions exceed the {MaxPixelDimension}px/{MaxPixelArea}px^2 limit." });
        }

        await using var decodeStream = await storage.OpenReadAsync(intent.QuarantineKey, cancellationToken);
        using var image = await Image.LoadAsync(decodeStream, cancellationToken);

        // Strip EXIF/GPS/ICC/XMP — docs/02: "decodes+re-encodes the image (stripping EXIF/GPS in
        // the process)". The re-encoded output below is bytes this application generated from
        // decoded pixel data, never a byte-for-byte copy of the uploaded file.
        image.Metadata.ExifProfile = null;
        image.Metadata.IccProfile = null;
        image.Metadata.XmpProfile = null;

        using var output = new MemoryStream();
        await image.SaveAsync(output, ResolveEncoder(detectedMime), cancellationToken);
        output.Position = 0;

        var cleanKey = $"clean/{Guid.CreateVersion7():N}";
        await storage.WriteAsync(cleanKey, output, cancellationToken);

        var attachment = new Attachment(
            intent.Id, intent.SiteId, intent.ParentResourceType, intent.ParentResourceId,
            cleanKey, detectedMime, output.Length, image.Width, image.Height, actorUserId.Value);
        attachmentsDb.Attachments.Add(attachment);
        intent.MarkActive();

        await attachmentsDb.SaveChangesAsync(cancellationToken);

        await auditWriter.WriteAsync(
            auditDb,
            new AuditEventEntry(
                ActorUserId: actorUserId,
                Action: "attachment.finalized",
                ResourceType: "Attachment",
                ResourceId: attachment.Id,
                SiteId: attachment.SiteId,
                CorrelationId: null,
                Reason: null,
                BeforeJson: null,
                AfterJson: JsonSerializer.Serialize(new { attachment.ParentResourceType, attachment.ParentResourceId, attachment.ContentType, attachment.ByteSize })),
            cancellationToken);

        await transactionScope.CommitAsync(cancellationToken);

        // Quarantine key is deleted only after the transaction holding the new Attachment row and
        // the intent's Active status has actually committed — a crash before commit just leaves an
        // orphaned quarantine object (harmless, never has a download route) rather than a clean key
        // with no committed row behind it.
        await storage.DeleteAsync(intent.QuarantineKey, cancellationToken);

        return Results.Created($"/attachments/{attachment.Id}", AttachmentResponse.From(attachment));
    }

    /// <summary>
    /// Persists the Rejected status and commits — a reject path still needs its state change to
    /// stick, not to be rolled back by <see cref="SharedTransactionScope"/>'s dispose-time rollback
    /// (which fires on any scope that reaches disposal without an explicit commit).
    /// </summary>
    private static async Task RejectAsync(
        AttachmentUploadIntent intent, AttachmentsDbContext attachmentsDb, IAttachmentStorage storage,
        SharedTransactionScope transactionScope, CancellationToken cancellationToken)
    {
        intent.Reject();
        await attachmentsDb.SaveChangesAsync(cancellationToken);
        await transactionScope.CommitAsync(cancellationToken);
        await storage.DeleteAsync(intent.QuarantineKey, cancellationToken);
    }

    private static IImageEncoder ResolveEncoder(string mime) => mime switch
    {
        "image/jpeg" => new JpegEncoder { Quality = 85 },
        "image/png" => new PngEncoder(),
        "image/webp" => new WebpEncoder(),
        _ => throw new InvalidOperationException($"No encoder for mime type {mime}.")
    };

    // ---------- Download ----------

    private static async Task<IResult> DownloadAsync(
        Guid id,
        HttpContext httpContext,
        ClaimsPrincipal user,
        AttachmentsDbContext attachmentsDb,
        WorkManagementDbContext workOrdersDb,
        IAttachmentStorage storage,
        IPermissionEvaluator permissions,
        CancellationToken cancellationToken)
    {
        var attachment = await attachmentsDb.Attachments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (attachment is null || attachment.UnlinkedAtUtc is not null)
        {
            return Results.NotFound();
        }

        var workOrder = await workOrdersDb.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => w.Id == attachment.ParentResourceId, cancellationToken);
        if (workOrder is null || !await CanReadWorkOrderAsync(workOrder, user, permissions, cancellationToken))
        {
            return Results.NotFound();
        }

        var stream = await storage.OpenReadAsync(attachment.CleanStorageKey, cancellationToken);
        var extension = attachment.ContentType switch
        {
            "image/jpeg" => "jpg",
            "image/png" => "png",
            "image/webp" => "webp",
            _ => "bin"
        };

        // Results.File sets Content-Disposition: attachment on its own (given a fileDownloadName);
        // X-Content-Type-Options isn't one of its defaults, so it's set explicitly here — docs/02's
        // two required download-hardening headers. The filename is generated, never client-supplied
        // (the original uploaded filename is display-only — see AttachmentUploadIntent — and never
        // reaches a response header).
        httpContext.Response.Headers.Append("X-Content-Type-Options", "nosniff");
        return Results.File(stream, attachment.ContentType, $"evidence-{attachment.Id:N}.{extension}", enableRangeProcessing: false);
    }

    // ---------- Unlink ----------

    private static async Task<IResult> UnlinkAsync(
        Guid id,
        HttpContext httpContext,
        IAntiforgery antiforgery,
        IConfiguration configuration,
        IPermissionEvaluator permissions,
        IAuditEventWriter auditWriter,
        CancellationToken cancellationToken)
    {
        if (!await AntiforgeryHelpers.HasValidAntiforgeryTokenAsync(httpContext, antiforgery))
        {
            return Results.BadRequest(new { error = "Invalid anti-forgery token." });
        }

        var connectionString = configuration.GetConnectionString("Cmms")
            ?? throw new InvalidOperationException("Connection string 'Cmms' is not configured.");

        await using var transactionScope = await SharedTransactionScope.BeginAsync(connectionString, cancellationToken);
        await using var attachmentsDb = transactionScope.CreateContext<AttachmentsDbContext>(options => new AttachmentsDbContext(options));
        await using var workOrdersDb = transactionScope.CreateContext<WorkManagementDbContext>(options => new WorkManagementDbContext(options));
        await using var auditDb = transactionScope.CreateContext<AuditDbContext>(options => new AuditDbContext(options));

        var attachment = await attachmentsDb.Attachments.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (attachment is null)
        {
            return Results.NotFound();
        }

        // Root-lock the parent Work Order (docs/02: "Activation, link, and unlink are ordinary Work
        // Order/Asset-root-locked mutations, per the concurrency protocol ... not a side-channel
        // write") so an unlink can never land mid-completion.
        var workOrder = await workOrdersDb.WorkOrders
            .FromSqlInterpolated($"SELECT * FROM work_management.work_orders WHERE id = {attachment.ParentResourceId} FOR UPDATE")
            .FirstOrDefaultAsync(cancellationToken);
        var actorUserId = permissions.GetUserId(httpContext.User);
        if (workOrder is null || actorUserId is null || !await CanWriteWorkOrderAsync(workOrder, httpContext.User, permissions, cancellationToken, PermissionCatalog.AttachmentsUnlink))
        {
            return Results.NotFound();
        }

        if (workOrder.Status is WorkOrderStatus.Closed or WorkOrderStatus.Cancelled)
        {
            return Results.Conflict(new { error = $"This Work Order is {workOrder.Status} and its evidence can no longer be changed." });
        }

        attachment.Unlink();
        await attachmentsDb.SaveChangesAsync(cancellationToken);

        await auditWriter.WriteAsync(
            auditDb,
            new AuditEventEntry(
                ActorUserId: actorUserId,
                Action: "attachment.unlinked",
                ResourceType: "Attachment",
                ResourceId: attachment.Id,
                SiteId: attachment.SiteId,
                CorrelationId: null,
                Reason: null,
                BeforeJson: null,
                AfterJson: null),
            cancellationToken);

        await transactionScope.CommitAsync(cancellationToken);

        return Results.NoContent();
    }

    // ---------- Shared authorization (inherits the parent Work Order's own authorization, per
    // docs/02's RolePermissionSeed "inherit_parent_authorization" note) ----------

    private static async Task<bool> CanReadWorkOrderAsync(WorkOrder workOrder, ClaimsPrincipal user, IPermissionEvaluator permissions, CancellationToken cancellationToken)
    {
        var userId = permissions.GetUserId(user);
        var canReadAll = await permissions.HasPermissionAsync(user, PermissionCatalog.WorkOrdersReadAll, workOrder.SiteId, cancellationToken);
        var canReadAssigned = userId == workOrder.AssigneeId &&
            await permissions.HasPermissionAsync(user, PermissionCatalog.WorkOrdersReadAssigned, workOrder.SiteId, cancellationToken);
        return canReadAll || canReadAssigned;
    }

    private static async Task<bool> CanWriteWorkOrderAsync(
        WorkOrder workOrder, ClaimsPrincipal user, IPermissionEvaluator permissions, CancellationToken cancellationToken,
        string attachmentPermission = PermissionCatalog.AttachmentsWrite)
    {
        var userId = permissions.GetUserId(user);
        if (userId is null || !await permissions.HasPermissionAsync(user, attachmentPermission, workOrder.SiteId, cancellationToken))
        {
            return false;
        }

        var role = await permissions.GetEffectiveRoleAsync(user, workOrder.SiteId, cancellationToken);
        return role is RoleCode.Admin or RoleCode.Planner || workOrder.AssigneeId == userId;
    }

    // ---------- Bounded read (dev/CI substitute for a presigned PUT's own size enforcement) ----------

    private sealed class BodyTooLargeException : Exception;

    private static async Task<byte[]> ReadBoundedAsync(Stream body, long maxBytes, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        long total = 0;
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new BodyTooLargeException();
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}

// ---------- Requests ----------

internal sealed record CreateUploadIntentRequest(string DeclaredContentType, string? OriginalFileName);

// ---------- Responses ----------

internal sealed record UploadIntentResponse(Guid Id, AttachmentUploadIntentStatus Status, DateTimeOffset ExpiresAtUtc)
{
    public static UploadIntentResponse From(AttachmentUploadIntent intent) => new(intent.Id, intent.Status, intent.ExpiresAtUtc);
}

internal sealed record AttachmentResponse(
    Guid Id,
    Guid ParentResourceId,
    string ContentType,
    long ByteSize,
    int PixelWidth,
    int PixelHeight,
    Guid UploadedByUserId,
    DateTimeOffset CreatedAtUtc)
{
    public static AttachmentResponse From(Attachment attachment) => new(
        attachment.Id, attachment.ParentResourceId, attachment.ContentType, attachment.ByteSize,
        attachment.PixelWidth, attachment.PixelHeight, attachment.UploadedByUserId, attachment.CreatedAtUtc);
}
