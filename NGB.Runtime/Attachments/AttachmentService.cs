using System.Globalization;
using System.Net.Mime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NGB.Attachments;
using NGB.Contracts.Attachments;
using NGB.Contracts.BusinessObjects;
using NGB.Core.AuditLog;
using NGB.Core.Security;
using NGB.Persistence.Attachments;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.BusinessObjects;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Attachments;

internal sealed class AttachmentService(
    IAttachmentRepository repository,
    IAttachmentObjectStorage storage,
    BusinessObjectContentAccess access,
    IUnitOfWork uow,
    IAuditLogService audit,
    AttachmentCleanupQueue cleanup,
    TimeProvider clock,
    IOptions<AttachmentOptions> options,
    ILogger<AttachmentService> logger)
    : IAttachmentService
{
    public async Task<BusinessObjectPage<AttachmentDto>> ListAsync(
        BusinessObjectRef target,
        int limit,
        Guid? cursor,
        CancellationToken ct)
    {
        BusinessObjectContentAccess.ValidatePage(limit, cursor);

        await access.RequireAsync(target, NgbSystemPermissions.AttachmentsRead, ct);
        var rows = await repository.ListAsync(target, limit + 1, cursor, ct);

        return new(
            rows.Take(limit).Select(ToDto).ToArray(),
            rows.Count > limit ? rows[limit - 1].Id : null);
    }

    public async Task<AttachmentUploadDto> CreateUploadAsync(
        CreateAttachmentUploadRequest request,
        CancellationToken ct)
    {
        var fileName = NormalizeFileName(request.FileName);
        var contentType = NormalizeContentType(request.ContentType);

        if (request.SizeBytes < 0 || request.SizeBytes > options.Value.MaxSizeBytes)
            throw Error("file_too_large", "Declared size exceeds the configured attachment limit.");

        await access.RequireAsync(request.Target, NgbSystemPermissions.AttachmentsCreate, ct);

        return await uow.ExecuteInUowTransactionAsync(async token =>
        {
            await repository.LockTargetAsync(request.Target, token);
            if (await repository.CountReservedAsync(request.Target, token) >= options.Value.MaxActivePerObject)
                throw Error("count_limit_exceeded", "The attachment limit has been reached. Remove an attachment or wait for abandoned uploads to expire.");

            var now = clock.GetUtcNow().UtcDateTime;
            var id = Guid.CreateVersion7();
            var actor = await access.ActorAsync(token);
            var row = new AttachmentRecord(
                id,
                request.Target,
                fileName,
                contentType,
                request.SizeBytes,
                $"attachments/{id:N}", $"uploads/{id:N}",
                AttachmentStatus.PendingUpload,
                now,
                actor,
                now.Add(options.Value.UploadLifetime));

            await repository.InsertAsync(row, token);

            var target = await storage.CreateUploadTargetAsync(
                row.UploadObjectKey,
                contentType,
                options.Value.UploadLifetime,
                token);

            await AuditAsync(row, "attachments.upload_requested", token);

            return new AttachmentUploadDto(id, target.Url, row.UploadExpiresAtUtc, target.Headers);
        }, ct);
    }

    public Task<AttachmentDto> CompleteAsync(Guid id, CancellationToken ct)
        => uow.ExecuteInUowTransactionAsync(async token =>
        {
            var row = await LoadAsync(id, NgbSystemPermissions.AttachmentsCreate, token);
            if (row.Status == AttachmentStatus.Ready)
                return ToDto(row);

            RequireNotDeleted(row);

            if (clock.GetUtcNow().UtcDateTime >= row.CreatedAtUtc.Add(options.Value.PendingStaleAge))
                throw Error("upload_expired", "This upload has expired. Start a new upload.", NgbErrorKind.Conflict);

            var stored = await storage.GetObjectInfoAsync(row.UploadObjectKey, token)
                ?? throw Error(
                    "upload_missing",
                    "The uploaded object was not found. Upload the file before completing.",
                     NgbErrorKind.Conflict);

            if (stored.SizeBytes != row.SizeBytes || stored.SizeBytes > options.Value.MaxSizeBytes)
            {
                logger.LogWarning(
                    "Attachment {AttachmentId} size mismatch: declared {DeclaredBytes}, actual {ActualBytes}.",
                    id,
                    row.SizeBytes,
                    stored.SizeBytes);

                throw Error("size_mismatch", "Uploaded size does not match the declared size.", NgbErrorKind.Conflict);
            }

            if (!string.Equals(stored.ContentType, row.ContentType, StringComparison.OrdinalIgnoreCase))
                throw Error("content_type_mismatch", "Uploaded content type does not match the declared type.", NgbErrorKind.Conflict);

            await storage.SealUploadAsync(row.UploadObjectKey, row.StorageObjectKey, stored.ETag, token);
            row = row with
            {
                Status = AttachmentStatus.Ready,
                CompletedAtUtc = clock.GetUtcNow().UtcDateTime
            };
            await repository.SaveAsync(row, token);

            // Delete the upload staging object only after the original PUT can no longer be replayed.
            await cleanup.EnqueueAsync(row, stagingOnly: true, token);
            await AuditAsync(row, "attachments.upload_completed", token);

            return ToDto(row);
        }, ct);

    public Task<AttachmentDownloadDto> DownloadAsync(Guid id, CancellationToken ct)
        => uow.ExecuteInUowTransactionAsync(async token =>
        {
            // Row lock serializes URL issuance with logical deletion.
            var row = await LoadAsync(id, NgbSystemPermissions.AttachmentsRead, token);

            RequireNotDeleted(row);

            if (row.Status != AttachmentStatus.Ready)
                throw Error("not_ready", "Attachment is not ready for download.", NgbErrorKind.Conflict);

            var url = await storage.CreateDownloadTargetAsync(
                row.StorageObjectKey,
                row.FileName,
                options.Value.DownloadLifetime,
                token);

            return new AttachmentDownloadDto(url, clock.GetUtcNow().UtcDateTime.Add(options.Value.DownloadLifetime));
        }, ct);

    public Task DeleteAsync(Guid id, CancellationToken ct)
        => uow.ExecuteInUowTransactionAsync(async token =>
        {
            var row = await LoadAsync(id, NgbSystemPermissions.AttachmentsDelete, token);
            if (row.Status == AttachmentStatus.Deleted)
                return;

            row = row with
            {
                Status = AttachmentStatus.Deleted, DeletedAtUtc = clock.GetUtcNow().UtcDateTime,
                DeletedByUserId = await access.ActorAsync(token)
            };

            await repository.SaveAsync(row, token);
            await cleanup.EnqueueAsync(row, stagingOnly: false, token);
            await AuditAsync(row, "attachments.deleted", token);
        }, ct);

    private async Task<AttachmentRecord> LoadAsync(Guid id, NgbPermissionKey permission, CancellationToken ct)
    {
        var row = await repository.GetAsync(id, true, ct)
            ?? throw Error("not_found", "Attachment was not found.", NgbErrorKind.NotFound);

        await access.RequireAsync(row.Target, permission, ct);

        return row;
    }

    private Task AuditAsync(AttachmentRecord row, string action, CancellationToken ct)
        => audit.WriteAsync(
            AuditEntityKind.Attachment,
            row.Id,
            action,
            metadata: new
            {
                row.Target,
                row.SizeBytes,
                row.ContentType
            },
            ct: ct);

    private static void RequireNotDeleted(AttachmentRecord row)
    {
        if (row.Status == AttachmentStatus.Deleted)
            throw Error("deleted", "Attachment has been deleted.", NgbErrorKind.Conflict);
    }

    internal static AttachmentDto ToDto(AttachmentRecord row)
        => new(
            row.Id,
            row.FileName,
            row.ContentType,
            row.SizeBytes,
            row.CreatedAtUtc,
            row.CreatedByUserId,
            row.CreatedByDisplayName);

    private static AttachmentException Error(string code, string message, NgbErrorKind kind = NgbErrorKind.Validation)
        => new($"attachments.{code}", message, kind);

    internal static string NormalizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1024)
            throw Error("invalid_file_name", "A file name of at most 255 characters is required.");

        var name = value.Replace('\\', '/').Split('/')[^1].Normalize();
        name = string.Concat(name
                .Where(c => !char.IsControl(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format))
            .Trim();

        if (name.Length is < 1 or > 255 || name is "." or "..")
            throw Error("invalid_file_name", "A file name of at most 255 characters is required.");

        return name;
    }

    internal static string NormalizeContentType(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "application/octet-stream";

        if (value.Length > 200 || value.Any(char.IsControl))
            throw Error("invalid_content_type", "Invalid content type.");

        try
        {
            return new ContentType(value).MediaType.ToLowerInvariant();
        }
        catch (FormatException)
        {
            throw Error("invalid_content_type", "Invalid content type.");
        }
    }
}
