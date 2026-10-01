using Dapper;
using NGB.Attachments;
using NGB.Contracts.BusinessObjects;
using NGB.Persistence.Attachments;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.UnitOfWork;

namespace NGB.PostgreSql.Attachments;

internal sealed class PostgresAttachmentRepository(IUnitOfWork uow) : IAttachmentRepository
{
    private const string Select = """
        SELECT a.id AS "Id",
            a.object_kind AS "ObjectKind",
            a.object_type_code AS "ObjectTypeCode",
            a.object_id AS "ObjectId",
            a.file_name AS "FileName",
            a.content_type AS "ContentType",
            a.size_bytes AS "SizeBytes",
            a.storage_object_key AS "StorageObjectKey",
            a.upload_object_key AS "UploadObjectKey",
            a.status AS "Status",
            a.created_at_utc AS "CreatedAtUtc",
            a.created_by_user_id AS "CreatedByUserId",
            a.upload_expires_at_utc AS "UploadExpiresAtUtc",
            a.completed_at_utc AS "CompletedAtUtc",
            a.deleted_at_utc AS "DeletedAtUtc",
            a.deleted_by_user_id AS "DeletedByUserId",
            a.storage_deleted_at_utc AS "StorageDeletedAtUtc", u.display_name AS "CreatedByDisplayName"
        FROM platform_attachments a LEFT JOIN platform_users u ON u.user_id = a.created_by_user_id
        """;

    public async Task<AttachmentRecord?> GetAsync(Guid id, bool forUpdate, CancellationToken ct)
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

    public async Task<IReadOnlyList<AttachmentRecord>> ListAsync(
        BusinessObjectRef target,
        int take,
        Guid? cursor,
        CancellationToken ct)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        var rows = await uow.Connection.QueryAsync<Row>(new CommandDefinition(
            Select + " WHERE a.object_kind = @Kind AND a.object_type_code = @TypeCode AND a.object_id = @Id AND a.status = 2"
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

    public async Task InsertAsync(AttachmentRecord row, CancellationToken ct)
    {
        await uow.EnsureOpenForTransactionAsync(ct);

        await uow.Connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO platform_attachments (id, object_kind, object_type_code, object_id, file_name, content_type, size_bytes, storage_object_key, upload_object_key, status, created_at_utc, created_by_user_id, upload_expires_at_utc, completed_at_utc, deleted_at_utc, deleted_by_user_id, storage_deleted_at_utc)
            VALUES (@Id, @ObjectKind, @ObjectTypeCode, @ObjectId, @FileName, @ContentType, @SizeBytes, @StorageObjectKey, @UploadObjectKey, @Status, @CreatedAtUtc, @CreatedByUserId, @UploadExpiresAtUtc, @CompletedAtUtc, @DeletedAtUtc, @DeletedByUserId, @StorageDeletedAtUtc);
            """,
            Parameters(row),
            uow.Transaction,
            cancellationToken: ct));
    }

    public async Task SaveAsync(AttachmentRecord row, CancellationToken ct)
    {
        await uow.EnsureOpenForTransactionAsync(ct);

        await uow.Connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE platform_attachments SET file_name = @FileName, content_type = @ContentType, size_bytes = @SizeBytes, storage_object_key = @StorageObjectKey, upload_object_key = @UploadObjectKey, status = @Status, upload_expires_at_utc = @UploadExpiresAtUtc, completed_at_utc = @CompletedAtUtc, deleted_at_utc = @DeletedAtUtc, deleted_by_user_id = @DeletedByUserId, storage_deleted_at_utc = @StorageDeletedAtUtc WHERE id = @Id;
            """,
            Parameters(row),
            uow.Transaction,
            cancellationToken: ct));
    }
    
    private static object Parameters(AttachmentRecord row)
        => new
        {
            row.Id,
            ObjectKind = (short)row.Target.Kind,
            ObjectTypeCode = row.Target.TypeCode,
            ObjectId = row.Target.Id,
            row.FileName,
            row.ContentType,
            row.SizeBytes,
            row.StorageObjectKey,
            row.UploadObjectKey,
            Status = (short)row.Status,
            row.CreatedAtUtc,
            row.CreatedByUserId,
            row.UploadExpiresAtUtc,
            row.CompletedAtUtc,
            row.DeletedAtUtc,
            row.DeletedByUserId,
            row.StorageDeletedAtUtc
        };

    public async Task LockTargetAsync(BusinessObjectRef target, CancellationToken ct)
    {
        await uow.EnsureOpenForTransactionAsync(ct);

        // Transaction-scoped advisory lock reserves slots without mutating or locking the parent row.
        await uow.Connection.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(hashtextextended(@Key, 3100));",
            new
            {
                Key = $"attachments:{(int)target.Kind}:{target.TypeCode}:{target.Id:N}"
            },
            uow.Transaction,
            cancellationToken: ct));
    }

    public async Task<long> CountReservedAsync(BusinessObjectRef target, CancellationToken ct)
    {
        await uow.EnsureOpenForTransactionAsync(ct);

        return await uow.Connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT count(*) FROM platform_attachments WHERE object_kind = @Kind AND object_type_code = @TypeCode AND object_id = @Id AND status IN (1, 2);",
            new
            {
                Kind = (short)target.Kind,
                target.TypeCode,
                target.Id
            },
            uow.Transaction,
            cancellationToken: ct));
    }

    public async Task<IReadOnlyList<AttachmentRecord>> LockStalePendingAsync(
        DateTime beforeUtc,
        int take,
        CancellationToken ct)
    {
        await uow.EnsureOpenForTransactionAsync(ct);

        var rows = await uow.Connection.QueryAsync<Row>(new CommandDefinition(
            Select + " WHERE a.status = 1 AND a.created_at_utc < @BeforeUtc ORDER BY a.created_at_utc, a.id LIMIT @Take FOR UPDATE OF a SKIP LOCKED;",
            new
            {
                BeforeUtc = beforeUtc,
                Take = Math.Clamp(take, 1, 100)
            },
            uow.Transaction,
            cancellationToken: ct));

        return rows
            .Select(r => r.Map())
            .ToArray();
    }

    private sealed class Row
    {
        public Guid Id { get; init; }
        public int ObjectKind { get; init; }
        public string ObjectTypeCode { get; init; } = null!;
        public Guid ObjectId { get; init; }
        public string FileName { get; init; } = null!;
        public string ContentType { get; init; } = null!;
        public long SizeBytes { get; init; }
        public string StorageObjectKey { get; init; } = null!;
        public string UploadObjectKey { get; init; } = null!;
        public int Status { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public Guid CreatedByUserId { get; init; }
        public DateTime UploadExpiresAtUtc { get; init; }
        public DateTime? CompletedAtUtc { get; init; }
        public DateTime? DeletedAtUtc { get; init; }
        public Guid? DeletedByUserId { get; init; }
        public DateTime? StorageDeletedAtUtc { get; init; }
        public string? CreatedByDisplayName { get; init; }

        public AttachmentRecord Map() => new(
            Id,
            new((BusinessObjectKind)ObjectKind, ObjectTypeCode, ObjectId),
            FileName,
            ContentType,
            SizeBytes,
            StorageObjectKey,
            UploadObjectKey,
            (AttachmentStatus)Status,
            CreatedAtUtc,
            CreatedByUserId,
            UploadExpiresAtUtc,
            CompletedAtUtc,
            DeletedAtUtc,
            DeletedByUserId,
            StorageDeletedAtUtc,
            CreatedByDisplayName);
    }
}
