using Dapper;
using NGB.Core.Catalogs;
using NGB.Core.Catalogs.Exceptions;
using NGB.Persistence.Catalogs;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.Catalogs;

public sealed class PostgresCatalogRepository(IUnitOfWork uow) : ICatalogRepository
{
    public async Task CreateAsync(CatalogRecord catalog, CancellationToken ct = default)
        => await CreateManyAsync([catalog], ct);

    public async Task CreateManyAsync(IReadOnlyList<CatalogRecord> catalogs, CancellationToken ct = default)
    {
        if (catalogs is null)
            throw new NgbArgumentRequiredException(nameof(catalogs));

        if (catalogs.Count == 0)
            return;

        var ids = new Guid[catalogs.Count];
        var catalogCodes = new string[catalogs.Count];
        var isDeleted = new bool[catalogs.Count];
        var createdAtUtc = new DateTime[catalogs.Count];
        var updatedAtUtc = new DateTime[catalogs.Count];

        for (var i = 0; i < catalogs.Count; i++)
        {
            var catalog = catalogs[i];

            if (string.IsNullOrWhiteSpace(catalog.CatalogCode))
                throw new NgbArgumentRequiredException(nameof(catalogs));

            catalog.CreatedAtUtc.EnsureUtc(nameof(catalog.CreatedAtUtc));
            catalog.UpdatedAtUtc.EnsureUtc(nameof(catalog.UpdatedAtUtc));

            ids[i] = catalog.Id;
            catalogCodes[i] = catalog.CatalogCode;
            isDeleted[i] = catalog.IsDeleted;
            createdAtUtc[i] = catalog.CreatedAtUtc;
            updatedAtUtc[i] = catalog.UpdatedAtUtc;
        }

        // Writes must be executed inside an active transaction to guarantee atomicity
        // across the common registry row and any module-provided typed storage.
        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
                           INSERT INTO catalogs(
                               id, catalog_code, is_deleted, created_at_utc, updated_at_utc
                           )
                           SELECT
                               x.id,
                               x.catalog_code,
                               x.is_deleted,
                               x.created_at_utc,
                               x.updated_at_utc
                           FROM UNNEST(
                               @Ids::uuid[],
                               @CatalogCodes::text[],
                               @IsDeleted::boolean[],
                               @CreatedAtUtc::timestamptz[],
                               @UpdatedAtUtc::timestamptz[]
                           ) AS x(id, catalog_code, is_deleted, created_at_utc, updated_at_utc);
                           """;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                Ids = ids,
                CatalogCodes = catalogCodes,
                IsDeleted = isDeleted,
                CreatedAtUtc = createdAtUtc,
                UpdatedAtUtc = updatedAtUtc
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        await uow.Connection.ExecuteAsync(cmd);
    }

    public async Task<CatalogRecord?> GetAsync(Guid catalogId, CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);
        
        const string sql = """
                           SELECT
                               id              AS Id,
                               catalog_code     AS CatalogCode,
                               is_deleted       AS IsDeleted,
                               created_at_utc   AS CreatedAtUtc,
                               updated_at_utc   AS UpdatedAtUtc
                           FROM catalogs
                           WHERE id = @Id;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { Id = catalogId },
            transaction: uow.Transaction,
            cancellationToken: ct);
        
        var row = await uow.Connection.QuerySingleOrDefaultAsync<CatalogRow>(cmd);
        return row?.ToRecord();
    }

    public async Task<CatalogRecord?> GetForUpdateAsync(Guid catalogId, CancellationToken ct = default)
    {
        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
                           SELECT
                               id              AS Id,
                               catalog_code     AS CatalogCode,
                               is_deleted       AS IsDeleted,
                               created_at_utc   AS CreatedAtUtc,
                               updated_at_utc   AS UpdatedAtUtc
                           FROM catalogs
                           WHERE id = @Id
                           FOR UPDATE;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { Id = catalogId },
            transaction: uow.Transaction,
            cancellationToken: ct);
        
        var row = await uow.Connection.QuerySingleOrDefaultAsync<CatalogRow>(cmd);
        return row?.ToRecord();
    }

    public async Task MarkForDeletionAsync(Guid catalogId, DateTime updatedAtUtc, CancellationToken ct = default)
    {
        updatedAtUtc.EnsureUtc(nameof(updatedAtUtc));
        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
                           UPDATE catalogs
                           SET is_deleted = TRUE,
                               updated_at_utc = @UpdatedAtUtc
                           WHERE id = @Id;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { Id = catalogId, UpdatedAtUtc = updatedAtUtc },
            transaction: uow.Transaction,
            cancellationToken: ct);
        
        var rows = await uow.Connection.ExecuteAsync(cmd);
        if (rows == 0)
            throw new CatalogNotFoundException(catalogId);
        
        if (rows != 1)
            throw new NgbInvariantViolationException("Unexpected row count while updating catalog state.",
                context: new Dictionary<string, object?> { ["catalogId"] = catalogId, ["rows"] = rows });
    }

    public async Task UnmarkForDeletionAsync(Guid catalogId, DateTime updatedAtUtc, CancellationToken ct = default)
    {
        updatedAtUtc.EnsureUtc(nameof(updatedAtUtc));
        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
                           UPDATE catalogs
                           SET is_deleted = FALSE,
                               updated_at_utc = @UpdatedAtUtc
                           WHERE id = @Id;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { Id = catalogId, UpdatedAtUtc = updatedAtUtc },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = await uow.Connection.ExecuteAsync(cmd);
        if (rows == 0)
            throw new CatalogNotFoundException(catalogId);
        
        if (rows != 1)
            throw new NgbInvariantViolationException("Unexpected row count while updating catalog state.",
                context: new Dictionary<string, object?> { ["catalogId"] = catalogId, ["rows"] = rows });
    }

    public async Task TouchAsync(Guid catalogId, DateTime updatedAtUtc, CancellationToken ct = default)
    {
        updatedAtUtc.EnsureUtc(nameof(updatedAtUtc));
        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
                           UPDATE catalogs
                           SET updated_at_utc = @UpdatedAtUtc
                           WHERE id = @Id;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { Id = catalogId, UpdatedAtUtc = updatedAtUtc },
            transaction: uow.Transaction,
            cancellationToken: ct);
        
        var rows = await uow.Connection.ExecuteAsync(cmd);
        if (rows == 0)
            throw new CatalogNotFoundException(catalogId);
        
        if (rows != 1)
            throw new NgbInvariantViolationException("Unexpected row count while updating catalog state.",
                context: new Dictionary<string, object?> { ["catalogId"] = catalogId, ["rows"] = rows });
    }

    private sealed class CatalogRow
    {
        public Guid Id { get; init; }
        public string CatalogCode { get; init; } = null!;
        public bool IsDeleted { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }

        public CatalogRecord ToRecord() => new()
        {
            Id = Id,
            CatalogCode = CatalogCode,
            IsDeleted = IsDeleted,
            CreatedAtUtc = CreatedAtUtc,
            UpdatedAtUtc = UpdatedAtUtc
        };
    }
}
