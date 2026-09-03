using Cmms.BuildingBlocks.Database;
using Cmms.Modules.Attachments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Cmms.Modules.Attachments.Infrastructure;

public sealed class AttachmentsDbContext(DbContextOptions<AttachmentsDbContext> options) : DbContext(options)
{
    public DbSet<AttachmentUploadIntent> UploadIntents => Set<AttachmentUploadIntent>();

    public DbSet<Attachment> Attachments => Set<Attachment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema(DatabaseSchemas.Attachments);

        builder.Entity<AttachmentUploadIntent>(entity =>
        {
            entity.ToTable(
                "upload_intents",
                table => table.HasCheckConstraint(
                    "ck_upload_intents_status",
                    "status IN ('Pending', 'Uploaded', 'Active', 'Expired', 'Rejected')"));
            entity.HasKey(intent => intent.Id);
            entity.Property(intent => intent.ParentResourceType).HasConversion<string>().HasMaxLength(20);
            entity.Property(intent => intent.QuarantineKey).HasMaxLength(100).IsRequired();
            entity.Property(intent => intent.DeclaredContentType).HasMaxLength(100).IsRequired();
            entity.Property(intent => intent.OriginalFileNameForDisplay).HasMaxLength(255);
            entity.Property(intent => intent.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(intent => intent.CreatedAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(intent => intent.ExpiresAtUtc).HasColumnType("timestamp with time zone");
            entity.HasIndex(intent => intent.QuarantineKey).IsUnique();
            entity.HasIndex(intent => new { intent.ParentResourceType, intent.ParentResourceId });
        });

        builder.Entity<Attachment>(entity =>
        {
            entity.ToTable("attachments");
            entity.HasKey(attachment => attachment.Id);
            entity.Property(attachment => attachment.ParentResourceType).HasConversion<string>().HasMaxLength(20);
            entity.Property(attachment => attachment.CleanStorageKey).HasMaxLength(100).IsRequired();
            entity.Property(attachment => attachment.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(attachment => attachment.CreatedAtUtc).HasColumnType("timestamp with time zone");
            entity.Property(attachment => attachment.UnlinkedAtUtc).HasColumnType("timestamp with time zone");
            entity.HasIndex(attachment => attachment.CleanStorageKey).IsUnique();
            entity.HasIndex(attachment => attachment.UploadIntentId).IsUnique();
            entity.HasIndex(attachment => new { attachment.ParentResourceType, attachment.ParentResourceId });
        });
    }
}
