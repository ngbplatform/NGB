using Microsoft.Extensions.Options;
using NGB.Application.Abstractions.Features;
using NGB.Attachments;
using NGB.Contracts.Attachments;
using NGB.Core.Features;
using NGB.Persistence.Attachments;
using NGB.Runtime.BusinessObjects;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Attachments;

internal sealed class AttachmentAuditService(
    INgbFeatureService features,
    IAttachmentRepository repository,
    ContentAuditAccess access,
    Func<IAttachmentObjectStorage> storage,
    IOptions<AttachmentOptions> options,
    TimeProvider clock)
    : IAttachmentAuditService
{
    public async Task<AttachmentDownloadDto> DownloadAsync(Guid id, CancellationToken ct)
    {
        await features.RequireAsync(NgbFeatures.Attachments, ct);
        var row = await repository.GetAsync(id, false, ct)
            ?? throw new AttachmentException("attachments.not_found", "Attachment was not found.", NgbErrorKind.NotFound);

        await access.RequireAttachmentAsync(row.Target, ct);

        if (!row.CompletedAtUtc.HasValue || row.StorageDeletedAtUtc.HasValue)
            throw new AttachmentException("attachments.not_available", "No retained file is available for this audit entry.", NgbErrorKind.Conflict);

        var url = await storage().CreateDownloadTargetAsync(
            row.StorageObjectKey,
            row.FileName,
            options.Value.DownloadLifetime,
            ct);

        return new AttachmentDownloadDto(url, clock.GetUtcNow().UtcDateTime.Add(options.Value.DownloadLifetime));
    }
}
