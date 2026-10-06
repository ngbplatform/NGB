using Dapper;
using NGB.Contracts.BusinessObjects;
using NGB.Notes;
using NGB.Persistence.Notes;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.UnitOfWork;

namespace NGB.PostgreSql.Notes;

internal sealed class PostgresNoteRepository(IUnitOfWork uow) : INoteRepository
{
    private const string Select = """
        SELECT a.id AS "Id",
            a.object_kind AS "ObjectKind",
            a.object_type_code AS "ObjectTypeCode",
            a.object_id AS "ObjectId",
            a.text AS "Text",
            a.version AS "Version",
            a.created_at_utc AS "CreatedAtUtc",
            a.created_by_user_id AS "CreatedByUserId",
            a.updated_at_utc AS "UpdatedAtUtc",
            a.updated_by_user_id AS "UpdatedByUserId",
            a.is_deleted AS "IsDeleted",
            a.deleted_at_utc AS "DeletedAtUtc",
            a.deleted_by_user_id AS "DeletedByUserId", u.display_name AS "CreatedByDisplayName"
        FROM platform_notes a LEFT JOIN platform_users u ON u.user_id = a.created_by_user_id
        """;

    public async Task<NoteRecord?> GetAsync(Guid id, bool forUpdate, CancellationToken ct)
    {
        if (forUpdate)
            uow.EnsureActiveTransaction();

        await uow.EnsureConnectionOpenAsync(ct);

        var row = await uow.Connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            Select + " WHERE a.id = @Id" + (forUpdate ? " FOR UPDATE OF a" : ""),
            new
            {
                Id = id
            },
            uow.Transaction,
            cancellationToken: ct));

        return row?.Map();
    }

    public async Task<IReadOnlyList<NoteRecord>> ListAsync(
        BusinessObjectRef target,
        int take,
        Guid? cursor,
        CancellationToken ct)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        var rows = await uow.Connection.QueryAsync<Row>(new CommandDefinition(
            Select + " WHERE a.object_kind = @Kind AND a.object_type_code = @TypeCode AND a.object_id = @Id AND NOT a.is_deleted"
                + (cursor.HasValue ? " AND a.id < @Cursor" : "") + " ORDER BY a.id DESC LIMIT @Take;",
            new
            {
                Kind = (short)target.Kind,
                target.TypeCode,
                target.Id,
                Cursor = cursor,
                Take = Math.Clamp(take, 1, 101)
            },
            uow.Transaction,
            cancellationToken: ct));

        return rows
            .Select(r => r.Map())
            .ToArray();
    }

    public async Task InsertAsync(NoteRecord row, CancellationToken ct)
    {
        await uow.EnsureOpenForTransactionAsync(ct);

        await uow.Connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO platform_notes (id, object_kind, object_type_code, object_id, text, version, created_at_utc, created_by_user_id, updated_at_utc, updated_by_user_id, is_deleted, deleted_at_utc, deleted_by_user_id)
            VALUES (@Id, @ObjectKind, @ObjectTypeCode, @ObjectId, @Text, @Version, @CreatedAtUtc, @CreatedByUserId, @UpdatedAtUtc, @UpdatedByUserId, @IsDeleted, @DeletedAtUtc, @DeletedByUserId);
            """,
            Parameters(row),
            uow.Transaction,
            cancellationToken: ct));
    }

    public async Task SaveAsync(NoteRecord row, CancellationToken ct)
    {
        await uow.EnsureOpenForTransactionAsync(ct);

        await uow.Connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE platform_notes SET text = @Text, version = @Version, updated_at_utc = @UpdatedAtUtc, updated_by_user_id = @UpdatedByUserId, is_deleted = @IsDeleted, deleted_at_utc = @DeletedAtUtc, deleted_by_user_id = @DeletedByUserId WHERE id = @Id;
            """,
            Parameters(row),
            uow.Transaction,
            cancellationToken: ct));
    }

    private static object Parameters(NoteRecord row)
        => new
        {
            row.Id,
            ObjectKind = (short)row.Target.Kind,
            ObjectTypeCode = row.Target.TypeCode,
            ObjectId = row.Target.Id,
            row.Text,
            row.Version,
            row.CreatedAtUtc,
            row.CreatedByUserId,
            row.UpdatedAtUtc,
            row.UpdatedByUserId,
            row.IsDeleted,
            row.DeletedAtUtc,
            row.DeletedByUserId
        };

    private sealed class Row
    {
        public Guid Id { get; init; }
        public int ObjectKind { get; init; }
        public string ObjectTypeCode { get; init; } = null!;
        public Guid ObjectId { get; init; }
        public string Text { get; init; } = null!;
        public long Version { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public Guid CreatedByUserId { get; init; }
        public DateTime? UpdatedAtUtc { get; init; }
        public Guid? UpdatedByUserId { get; init; }
        public bool IsDeleted { get; init; }
        public DateTime? DeletedAtUtc { get; init; }
        public Guid? DeletedByUserId { get; init; }
        public string? CreatedByDisplayName { get; init; }

        public NoteRecord Map() => new(
            Id,
            new((BusinessObjectKind)ObjectKind, ObjectTypeCode, ObjectId),
            Text,
            Version,
            CreatedAtUtc,
            CreatedByUserId,
            UpdatedAtUtc,
            UpdatedByUserId,
            IsDeleted,
            DeletedAtUtc,
            DeletedByUserId,
            CreatedByDisplayName);
    }
}
