using Microsoft.Extensions.Options;
using NGB.Contracts.BusinessObjects;
using NGB.Contracts.Notes;
using NGB.Core.AuditLog;
using NGB.Core.Security;
using NGB.Notes;
using NGB.Persistence.Notes;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.BusinessObjects;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Notes;

internal sealed class NoteService(
    INoteRepository repository,
    BusinessObjectContentAccess access,
    IUnitOfWork uow,
    IAuditLogService audit,
    TimeProvider clock,
    IOptions<NoteOptions> options)
    : INoteService
{
    public async Task<BusinessObjectPage<NoteDto>> ListAsync(
        BusinessObjectRef target,
        int limit,
        Guid? cursor,
        CancellationToken ct)
    {
        BusinessObjectContentAccess.ValidatePage(limit, cursor);

        await access.RequireAsync(target, NgbSystemPermissions.NotesRead, ct);
        var rows = await repository.ListAsync(target, limit + 1, cursor, ct);

        return new(
            rows.Take(limit).Select(ToDto).ToArray(),
            rows.Count > limit ? rows[limit - 1].Id : null);
    }

    public async Task<NoteDto> CreateAsync(CreateNoteRequest request, CancellationToken ct)
    {
        var text = ValidateText(request.Text);
        await access.RequireAsync(request.Target, NgbSystemPermissions.NotesCreate, ct);

        return await uow.ExecuteInUowTransactionAsync(async token =>
        {
            var row = new NoteRecord(
                Guid.CreateVersion7(),
                request.Target,
                text, 1,
                clock.GetUtcNow().UtcDateTime,
                await access.ActorAsync(token));

            await repository.InsertAsync(row, token);
            await AuditAsync(row, "notes.created", token);

            return ToDto(row);
        }, ct);
    }

    public async Task<NoteDto> UpdateAsync(Guid id, UpdateNoteRequest request, CancellationToken ct)
    {
        var text = ValidateText(request.Text);

        return await uow.ExecuteInUowTransactionAsync(async token =>
        {
            var row = await LoadAsync(id, NgbSystemPermissions.NotesUpdate, token);
            RequireVersion(row, request.Version);

            row = row with
            { 
                Text = text,
                Version = row.Version + 1,
                UpdatedAtUtc = clock.GetUtcNow().UtcDateTime,
                UpdatedByUserId = await access.ActorAsync(token) 
            };

            await repository.SaveAsync(row, token);
            await AuditAsync(row, "notes.updated", token);

            return ToDto(row);
        }, ct);
    }

    public Task DeleteAsync(Guid id, long version, CancellationToken ct)
        => uow.ExecuteInUowTransactionAsync(async token =>
        {
            var row = await LoadAsync(id, NgbSystemPermissions.NotesDelete, token);
            if (row.IsDeleted)
                return;

            RequireVersion(row, version);

            row = row with 
            { 
                IsDeleted = true,
                Version = row.Version + 1,
                DeletedAtUtc = clock.GetUtcNow().UtcDateTime,
                DeletedByUserId = await access.ActorAsync(token)
            };

            await repository.SaveAsync(row, token);
            await AuditAsync(row, "notes.deleted", token);
        }, ct);

    private async Task<NoteRecord> LoadAsync(Guid id, NgbPermissionKey permission, CancellationToken ct)
    {
        var row = await repository.GetAsync(id, true, ct)
            ?? throw new NoteException("notes.not_found", "Note was not found.", NgbErrorKind.NotFound);

        await access.RequireAsync(row.Target, permission, ct);

        return row;
    }

    private static void RequireVersion(NoteRecord row, long version)
    {
        if (row.IsDeleted)
            throw new NoteException("notes.deleted", "Note has been deleted.", NgbErrorKind.Conflict);

        if (version != row.Version)
            throw new NoteException("notes.version_conflict", "Note changed. Reload before saving.", NgbErrorKind.Conflict);
    }

    private string ValidateText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Contains('\0'))
            throw new NoteException("notes.invalid_text", "A nonempty plain-text note is required.");

        if (text.Length > options.Value.MaxTextLength)
            throw new NoteException("notes.too_long", $"Notes must be at most {options.Value.MaxTextLength} characters.");

        return text.Trim();
    }

    private Task AuditAsync(NoteRecord row, string action, CancellationToken ct)
        => audit.WriteAsync(
            AuditEntityKind.Note,
            row.Id,
            action,
            metadata: new
            {
                row.Target,
                row.Version,
                TextLength = row.Text.Length
            },
            ct: ct);

    private static NoteDto ToDto(NoteRecord row) 
        => new(
            row.Id,
            row.Text,
            row.Version,
            row.CreatedAtUtc,
            row.CreatedByUserId,
            row.CreatedByDisplayName,
            row.UpdatedAtUtc,
            row.UpdatedByUserId);
}
