using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NGB.Attachments;
using NGB.Persistence.Attachments;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.BusinessObjects;
using NGB.Runtime.UnitOfWork;

namespace NGB.Runtime.Attachments;

internal sealed class AttachmentUploadExpirationService(
    IAttachmentRepository repository,
    IUnitOfWork uow,
    IAuditLogService audit,
    TimeProvider clock,
    IOptions<AttachmentOptions> options,
    ILogger<AttachmentUploadExpirationService> logger)
    : IAttachmentUploadExpirationService
{
    public Task<int> ExpirePendingAsync(CancellationToken ct)
        => uow.ExecuteInUowTransactionAsync(async token =>
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var rows = await repository.LockStalePendingAsync(
                now - options.Value.PendingStaleAge,
                options.Value.ExpirationBatchSize,
                token);

            foreach (var row in rows)
            {
                var deleted = row with
                {
                    Status = AttachmentStatus.Deleted,
                    DeletedAtUtc = now
                };

                await repository.SaveAsync(deleted, token);
                await ContentAudit.WriteAttachmentAsync(
                    audit,
                    deleted,
                    row.Status,
                    "attachments.upload_expired",
                    token);
            }

            if (rows.Count > 0)
                logger.LogInformation("Expired {Count} abandoned attachment uploads.", rows.Count);

            return rows.Count;
        }, ct);
}
