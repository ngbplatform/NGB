using NGB.Contracts.BusinessObjects;
using NGB.Contracts.Notes;
using NGB.Tools.Exceptions;

namespace NGB.Notes;

public sealed record NoteRecord(
    Guid Id,
    BusinessObjectRef Target,
    string Text,
    long Version,
    DateTime CreatedAtUtc,
    Guid CreatedByUserId,
    DateTime? UpdatedAtUtc = null,
    Guid? UpdatedByUserId = null,
    bool IsDeleted = false,
    DateTime? DeletedAtUtc = null,
    Guid? DeletedByUserId = null,
    string? CreatedByDisplayName = null);

public sealed class NoteOptions
{
    public int MaxTextLength { get; set; } = 10000;
    public bool IsValid() => MaxTextLength is >= 1 and <= 100000;
}

public sealed class NoteException(string code, string message, NgbErrorKind kind = NgbErrorKind.Validation)
    : NgbException(message, code, kind);

public interface INoteService
{
    Task<BusinessObjectPage<NoteDto>> ListAsync(
        BusinessObjectRef target,
        int limit,
        Guid? cursor,
        CancellationToken ct);

    Task<NoteDto> CreateAsync(CreateNoteRequest request, CancellationToken ct);
    Task<NoteDto> UpdateAsync(Guid id, UpdateNoteRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, long version, CancellationToken ct);
}
