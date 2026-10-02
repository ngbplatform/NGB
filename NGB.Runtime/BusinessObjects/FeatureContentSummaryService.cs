using NGB.Application.Abstractions.BusinessObjects;
using NGB.Application.Abstractions.Features;
using NGB.Contracts.BusinessObjects;
using NGB.Core.Features;

namespace NGB.Runtime.BusinessObjects;

internal sealed class FeatureContentSummaryService(
    INgbFeatureService features,
    Func<BusinessObjectContentSummaryService> service)
    : IBusinessObjectContentSummaryService
{
    public async Task<BusinessObjectContentSummary> GetAsync(BusinessObjectRef target, CancellationToken ct)
    {
        var attachments = await features.IsEnabledAsync(NgbFeatures.Attachments, ct);
        var notes = await features.IsEnabledAsync(NgbFeatures.Notes, ct);

        if (!attachments && !notes)
            return new BusinessObjectContentSummary(null, null);

        return await service().GetAsync(target, attachments, notes, ct);
    }
}
