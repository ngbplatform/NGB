using NGB.Notes;
using NGB.Contracts.BusinessObjects;

namespace NGB.Persistence.Notes;

public interface INoteRepository
{
    Task<NoteRecord?> GetAsync(Guid id, bool forUpdate, CancellationToken ct);
    Task<IReadOnlyList<NoteRecord>> ListAsync(BusinessObjectRef target, int take, Guid? cursor, CancellationToken ct);
    Task InsertAsync(NoteRecord record, CancellationToken ct);
    Task SaveAsync(NoteRecord record, CancellationToken ct);
}
