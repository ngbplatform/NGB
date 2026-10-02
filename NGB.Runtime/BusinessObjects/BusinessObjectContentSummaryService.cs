using NGB.Application.Abstractions.BusinessObjects;
using NGB.Contracts.BusinessObjects;
using NGB.Core.Security;
using NGB.Persistence.Attachments;
using NGB.Runtime.Security;

namespace NGB.Runtime.BusinessObjects;

internal sealed class BusinessObjectContentSummaryService(
    IBusinessObjectResolver resolver,
    INgbAccessChecker access,
    IBusinessObjectContentSummaryReader reader)
    : IBusinessObjectContentSummaryService
{
    public async Task<BusinessObjectContentSummary> GetAsync(BusinessObjectRef target, CancellationToken ct)
    {
        await resolver.ResolveAsync(target, ct);
        var permissions = await access.GetSnapshotAsync(ct);
        
        return await reader.GetAsync(
            target,
            permissions.Has(NgbSystemPermissions.AttachmentsRead),
            permissions.Has(NgbSystemPermissions.NotesRead),
            ct);
    }
}
