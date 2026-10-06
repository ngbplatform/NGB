using NGB.Contracts.BusinessObjects;

namespace NGB.Contracts.Notes;

public sealed record NoteDto(
    Guid Id,
    string Text,
    long Version,
    DateTime CreatedAtUtc,
    Guid CreatedByUserId,
    string? CreatedByDisplayName,
    DateTime? UpdatedAtUtc,
    Guid? UpdatedByUserId);

public sealed record CreateNoteRequest(BusinessObjectRef Target, string Text);
public sealed record UpdateNoteRequest(string Text, long Version);
