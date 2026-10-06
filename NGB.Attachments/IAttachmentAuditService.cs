using NGB.Contracts.Attachments;

namespace NGB.Attachments;

/// <summary>Issues authorized audit downloads, including retained files marked for deletion.</summary>
public interface IAttachmentAuditService
{
    Task<AttachmentDownloadDto> DownloadAsync(Guid id, CancellationToken ct);
}
