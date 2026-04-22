using Dapper;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.Documents;

public sealed class PostgresDocumentRepository(IUnitOfWork uow) : IDocumentRepository
{
    public async Task CreateAsync(DocumentRecord doc, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(doc.TypeCode))
            throw new NgbArgumentRequiredException(nameof(doc));

        doc.DateUtc.EnsureUtc(nameof(doc.DateUtc));
        doc.CreatedAtUtc.EnsureUtc(nameof(doc.CreatedAtUtc));
        doc.UpdatedAtUtc.EnsureUtc(nameof(doc.UpdatedAtUtc));
        
        if (doc.PostedAtUtc is not null)
            doc.PostedAtUtc.Value.EnsureUtc(nameof(doc.PostedAtUtc));
        
        if (doc.MarkedForDeletionAtUtc is not null)
            doc.MarkedForDeletionAtUtc.Value.EnsureUtc(nameof(doc.MarkedForDeletionAtUtc));

        // Writes must be executed inside an active transaction to guarantee atomicity
        // across the common registry row and any module-provided typed storage.
        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
                           INSERT INTO documents(
                               id, type_code, number, date_utc,
                               status, posted_at_utc, marked_for_deletion_at_utc,
                               created_at_utc, updated_at_utc
                           )
                           VALUES (
                               @Id, @TypeCode, @Number, @DateUtc,
                               @Status, @PostedAtUtc, @MarkedForDeletionAtUtc,
                               @CreatedAtUtc, @UpdatedAtUtc
                           );
                           """;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                doc.Id,
                doc.TypeCode,
                doc.Number,
                doc.DateUtc,
                Status = (short)doc.Status,
                doc.PostedAtUtc,
                doc.MarkedForDeletionAtUtc,
                doc.CreatedAtUtc,
                doc.UpdatedAtUtc
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.Connection.ExecuteAsync(cmd);
    }

    public async Task<DocumentRecord?> GetAsync(Guid documentId, CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);
        
        const string sql = """
                           SELECT
                               id                  AS Id,
                               type_code           AS TypeCode,
                               number              AS Number,
                               date_utc            AS DateUtc,
                               status              AS Status,
                               created_at_utc      AS CreatedAtUtc,
                               updated_at_utc      AS UpdatedAtUtc,
                               posted_at_utc       AS PostedAtUtc,
                               marked_for_deletion_at_utc AS MarkedForDeletionAtUtc
                           FROM documents
                           WHERE id = @Id;
                           """;

        var cmd = new CommandDefinition(sql, new { Id = documentId }, transaction: uow.Transaction, cancellationToken: ct);
        var row = await uow.Connection.QuerySingleOrDefaultAsync<DocumentRow>(cmd);
        return row?.ToRecord();
    }

    public async Task<DocumentRecord?> GetForUpdateAsync(Guid documentId, CancellationToken ct = default)
    {
        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
                           SELECT
                               id                  AS Id,
                               type_code           AS TypeCode,
                               number              AS Number,
                               date_utc            AS DateUtc,
                               status              AS Status,
                               created_at_utc      AS CreatedAtUtc,
                               updated_at_utc      AS UpdatedAtUtc,
                               posted_at_utc       AS PostedAtUtc,
                               marked_for_deletion_at_utc AS MarkedForDeletionAtUtc
                           FROM documents
                           WHERE id = @Id
                           FOR UPDATE;
                           """;

        var cmd = new CommandDefinition(sql, new { Id = documentId }, transaction: uow.Transaction, cancellationToken: ct);
        var row = await uow.Connection.QuerySingleOrDefaultAsync<DocumentRow>(cmd);
        return row?.ToRecord();
    }

    public async Task UpdateStatusAsync(
        Guid documentId,
        DocumentStatus status,
        DateTime updatedAtUtc,
        DateTime? postedAtUtc,
        DateTime? markedForDeletionAtUtc,
        CancellationToken ct = default)
    {
        await uow.EnsureOpenForTransactionAsync(ct);

        updatedAtUtc.EnsureUtc(nameof(updatedAtUtc));
        
        if (postedAtUtc is not null)
            postedAtUtc.Value.EnsureUtc(nameof(postedAtUtc));
        
        if (markedForDeletionAtUtc is not null)
            markedForDeletionAtUtc.Value.EnsureUtc(nameof(markedForDeletionAtUtc));

        const string sql = """
                           UPDATE documents
                           SET status = @Status,
                               updated_at_utc = @UpdatedAtUtc,
                               posted_at_utc = @PostedAtUtc,
                               marked_for_deletion_at_utc = @MarkedForDeletionAtUtc
                           WHERE id = @Id;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                Id = documentId,
                Status = (short)status,
                UpdatedAtUtc = updatedAtUtc,
                PostedAtUtc = postedAtUtc,
                MarkedForDeletionAtUtc = markedForDeletionAtUtc
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = await uow.Connection.ExecuteAsync(cmd);
        if (rows == 0)
            throw new DocumentNotFoundException(documentId);
        
        if (rows != 1)
            throw new NgbInvariantViolationException("Unexpected row count while updating document status.",
                context: new Dictionary<string, object?> { ["documentId"] = documentId, ["rows"] = rows });
    }

    public async Task<bool> TrySetNumberAsync(
        Guid documentId,
        string number,
        DateTime updatedAtUtc,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(number))
            throw new NgbArgumentRequiredException(nameof(number));

        await uow.EnsureOpenForTransactionAsync(ct);

        updatedAtUtc.EnsureUtc(nameof(updatedAtUtc));

        const string sql = """
                           UPDATE documents
                           SET number = @Number,
                               updated_at_utc = @UpdatedAtUtc
                           WHERE id = @Id
                             AND number IS NULL;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                Id = documentId,
                Number = number,
                UpdatedAtUtc = updatedAtUtc
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = await uow.Connection.ExecuteAsync(cmd);
        if (rows > 1)
            throw new NgbInvariantViolationException("Unexpected row count while setting document number.",
                context: new Dictionary<string, object?> { ["documentId"] = documentId, ["rows"] = rows });

        return rows == 1;
    }

    
    public async Task<bool> UpdateDraftHeaderAsync(
        Guid documentId,
        string? number,
        DateTime dateUtc,
        DateTime updatedAtUtc,
        CancellationToken ct = default)
    {
        await uow.EnsureOpenForTransactionAsync(ct);

        dateUtc.EnsureUtc(nameof(dateUtc));
        updatedAtUtc.EnsureUtc(nameof(updatedAtUtc));

        if (number is not null)
            number = string.IsNullOrWhiteSpace(number) ? null : number.Trim();

        const string sql = """
                           UPDATE documents
                           SET number = @Number,
                               date_utc = @DateUtc,
                               updated_at_utc = @UpdatedAtUtc
                           WHERE id = @Id
                             AND status = @DraftStatus;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                Id = documentId,
                Number = number,
                DateUtc = dateUtc,
                UpdatedAtUtc = updatedAtUtc,
                DraftStatus = (short)DocumentStatus.Draft
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = await uow.Connection.ExecuteAsync(cmd);
        if (rows > 1)
            throw new NgbInvariantViolationException("Unexpected row count while updating draft header.",
                context: new Dictionary<string, object?> { ["documentId"] = documentId, ["rows"] = rows });

        return rows == 1;
    }

    public async Task<bool> TryDeleteAsync(Guid documentId, CancellationToken ct = default)
    {
        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = "DELETE FROM documents WHERE id = @Id;";

        var cmd = new CommandDefinition(
            sql,
            new { Id = documentId },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = await uow.Connection.ExecuteAsync(cmd);
        if (rows > 1)
            throw new NgbInvariantViolationException("Unexpected row count while deleting document.",
                context: new Dictionary<string, object?> { ["documentId"] = documentId, ["rows"] = rows });

        return rows == 1;
    }

private sealed class DocumentRow
    {
        public Guid Id { get; init; }
        public string TypeCode { get; init; } = null!;
        public string? Number { get; init; }
        public DateTime DateUtc { get; init; }
        public short Status { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }
        public DateTime? PostedAtUtc { get; init; }
        public DateTime? MarkedForDeletionAtUtc { get; init; }

        public DocumentRecord ToRecord() => new()
        {
            Id = Id,
            TypeCode = TypeCode,
            Number = Number,
            DateUtc = DateUtc,
            Status = (DocumentStatus)Status,
            CreatedAtUtc = CreatedAtUtc,
            UpdatedAtUtc = UpdatedAtUtc,
            PostedAtUtc = PostedAtUtc,
            MarkedForDeletionAtUtc = MarkedForDeletionAtUtc
        };
    }
}
