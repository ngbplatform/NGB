using NGB.Application.Abstractions.Features;
using NGB.Attachments;
using NGB.Contracts.Attachments;
using NGB.Contracts.BusinessObjects;
using NGB.Core.Features;

namespace NGB.Runtime.Attachments;

internal sealed class FeatureAttachmentService(INgbFeatureService features, Func<AttachmentService> service)
    : IAttachmentService
{
    public async Task<BusinessObjectPage<AttachmentDto>> ListAsync(
        BusinessObjectRef target,
        int limit,
        Guid? cursor,
        CancellationToken ct)
    {
        await features.RequireAsync(NgbFeatures.Attachments, ct);
        return await service().ListAsync(target, limit, cursor, ct);
    }

    public async Task<AttachmentUploadDto> CreateUploadAsync(
        CreateAttachmentUploadRequest request,
        CancellationToken ct)
    {
        await features.RequireAsync(NgbFeatures.Attachments, ct);
        return await service().CreateUploadAsync(request, ct);
    }

    public async Task<AttachmentDto> CompleteAsync(Guid id, CancellationToken ct)
    {
        await features.RequireAsync(NgbFeatures.Attachments, ct);
        return await service().CompleteAsync(id, ct);
    }

    public async Task<AttachmentDownloadDto> DownloadAsync(Guid id, CancellationToken ct)
    {
        await features.RequireAsync(NgbFeatures.Attachments, ct);
        return await service().DownloadAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await features.RequireAsync(NgbFeatures.Attachments, ct);
        await service().DeleteAsync(id, ct);
    }
}
