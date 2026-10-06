using NGB.Attachments;
using NGB.Contracts.BusinessObjects;
using NGB.Core.AuditLog;
using NGB.Notes;
using NGB.Runtime.AuditLog;

namespace NGB.Runtime.BusinessObjects;

internal static class ContentAudit
{
    public static AuditEntityKind ParentKind(BusinessObjectRef target)
        => target.Kind == BusinessObjectKind.CatalogItem ? AuditEntityKind.Catalog : AuditEntityKind.Document;

    public static Task WriteNoteAsync(
        IAuditLogService audit,
        NoteRecord row,
        NoteRecord? before,
        string action,
        CancellationToken ct)
    {
        var changes = new List<AuditFieldChange>
        {
            AuditLogService.Change("note.text", before?.Text, row.Text),
            AuditLogService.Change("note.version", before?.Version, row.Version)
        };

        if (before is null || before.IsDeleted != row.IsDeleted)
            changes.Add(AuditLogService.Change("note.is_deleted", before?.IsDeleted, row.IsDeleted));

        return audit.WriteAsync(
            ParentKind(row.Target),
            row.Target.Id,
            action,
            changes,
            metadata: new { row.Target, NoteId = row.Id, row.Version },
            ct: ct);
    }

    public static Task WriteAttachmentAsync(
        IAuditLogService audit,
        AttachmentRecord row,
        AttachmentStatus? before,
        string action,
        CancellationToken ct)
        => audit.WriteAsync(
            ParentKind(row.Target),
            row.Target.Id,
            action,
            changes:
            [
                AuditLogService.Change("attachment.file_name", null, row.FileName),
                AuditLogService.Change("attachment.content_type", null, row.ContentType),
                AuditLogService.Change("attachment.size_bytes", null, row.SizeBytes),
                AuditLogService.Change("attachment.status", before, row.Status),
                AuditLogService.Change("attachment.is_deleted", before == AttachmentStatus.Deleted, row.Status == AttachmentStatus.Deleted)
            ],
            metadata: new
            {
                row.Target,
                AttachmentId = row.Id,
                row.FileName,
                DownloadAvailable = row.CompletedAtUtc.HasValue && !row.StorageDeletedAtUtc.HasValue
            },
            ct: ct);
}
