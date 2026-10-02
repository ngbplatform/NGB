using NGB.Application.Abstractions.Features;
using NGB.Contracts.BusinessObjects;
using NGB.Contracts.Notes;
using NGB.Core.Features;
using NGB.Notes;

namespace NGB.Runtime.Notes;

internal sealed class FeatureNoteService(INgbFeatureService features, Func<NoteService> service)
    : INoteService
{
    public async Task<BusinessObjectPage<NoteDto>> ListAsync(
        BusinessObjectRef target,
        int limit,
        Guid? cursor,
        CancellationToken ct)
    {
        await features.RequireAsync(NgbFeatures.Notes, ct);
        return await service().ListAsync(target, limit, cursor, ct);
    }

    public async Task<NoteDto> CreateAsync(CreateNoteRequest request, CancellationToken ct)
    {
        await features.RequireAsync(NgbFeatures.Notes, ct);
        return await service().CreateAsync(request, ct);
    }

    public async Task<NoteDto> UpdateAsync(Guid id, UpdateNoteRequest request, CancellationToken ct)
    {
        await features.RequireAsync(NgbFeatures.Notes, ct);
        return await service().UpdateAsync(id, request, ct);
    }

    public async Task DeleteAsync(Guid id, long version, CancellationToken ct)
    {
        await features.RequireAsync(NgbFeatures.Notes, ct);
        await service().DeleteAsync(id, version, ct);
    }
}
