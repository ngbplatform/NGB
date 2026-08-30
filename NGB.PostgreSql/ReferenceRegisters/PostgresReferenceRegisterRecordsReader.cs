using System.Collections.Concurrent;
using Dapper;
using NGB.Core.Dimensions;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Internal;
using NGB.PostgreSql.Schema;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.ReferenceRegisters.Exceptions;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.ReferenceRegisters;

/// <summary>
/// SliceLast reader for per-reference-register records tables (refreg_*__records).
///
/// Works both inside and outside a transaction; if the table has not been created yet, returns null.
/// </summary>
public sealed class PostgresReferenceRegisterRecordsReader(
    IUnitOfWork uow,
    IReferenceRegisterRepository registers,
    IReferenceRegisterFieldRepository fieldsRepo,
    ReferenceRegisterMetadataCache? metadataCache = null,
    PostgresRelationPresenceCache? relationPresenceCache = null)
    : IReferenceRegisterRecordsReader
{
    private readonly ReferenceRegisterMetadataCache _metadataCache = metadataCache
        ?? new ReferenceRegisterMetadataCache(TimeProvider.System);
    private readonly PostgresRelationPresenceCache _relationPresenceCache = relationPresenceCache
        ?? new PostgresRelationPresenceCache(TimeProvider.System);
    private readonly ConcurrentDictionary<Guid, ReferenceRegisterMetadataContext> _localMetadata = new();

    public async Task<ReferenceRegisterRecordRead?> SliceLastAsync(
        Guid registerId,
        Guid dimensionSetId,
        DateTime asOfUtc,
        Guid? recorderDocumentId = null,
        CancellationToken ct = default)
    {
        registerId.EnsureNonEmpty(nameof(registerId));
        if (dimensionSetId == Guid.Empty)
        {
            // Guid.Empty is a valid DimensionSetId ("empty set").
            // We still allow reading SliceLast for it.
        }

        asOfUtc.EnsureUtc(nameof(asOfUtc));
        
        await uow.EnsureConnectionOpenAsync(ct);

        var context = await GetMetadataAsync(registerId, ct);
        var reg = context.Register;

        if (reg.RecordMode == ReferenceRegisterRecordMode.SubordinateToRecorder)
        {
            if (recorderDocumentId is null || recorderDocumentId.Value == Guid.Empty)
                throw new ReferenceRegisterRecordsValidationException(registerId, reason: "recorder_required", details: new { recordMode = reg.RecordMode });
        }
        else
        {
            if (recorderDocumentId is not null)
                throw new ReferenceRegisterRecordsValidationException(registerId, reason: "recorder_forbidden", details: new { recordMode = reg.RecordMode });
        }

        var table = context.RecordsTable;
        if (!await TableExistsAsync(table, ct))
            return null;

        var fields = context.Fields;

        var fieldsSelect = fields.Count == 0
            ? string.Empty
            : ", " + string.Join(", ", fields.Select(f => $"{f.ColumnCode} AS \"{f.ColumnCode}\""));

        var (wherePeriod, orderByPeriod, bucketAsOf) = BuildPeriodClause(reg.Periodicity, asOfUtc);

        var whereRecorder = reg.RecordMode == ReferenceRegisterRecordMode.SubordinateToRecorder
            ? "AND t.recorder_document_id = @RecorderDocumentId"
            : "AND t.recorder_document_id IS NULL";

        var sql = $"""
                  SELECT
                      record_id            AS "RecordId",
                      dimension_set_id     AS "DimensionSetId",
                      period_utc           AS "PeriodUtc",
                      period_bucket_utc    AS "PeriodBucketUtc",
                      recorder_document_id AS "RecorderDocumentId",
                      recorded_at_utc      AS "RecordedAtUtc",
                      is_deleted           AS "IsDeleted"{fieldsSelect}
                  FROM {table} t
                  WHERE
                      t.dimension_set_id = @DimensionSetId
                      {whereRecorder}
                      AND t.recorded_at_utc <= @AsOfUtc
                      {wherePeriod}
                  ORDER BY {orderByPeriod} t.recorded_at_utc DESC, t.record_id DESC
                  LIMIT 1;
                  """;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                DimensionSetId = dimensionSetId,
                RecorderDocumentId = recorderDocumentId,
                AsOfUtc = asOfUtc,
                BucketAsOfUtc = bucketAsOf
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = await uow.Connection.QueryAsync(cmd);
        var row = rows.FirstOrDefault();
        if (row is null)
            return null;

        return MapRow((IDictionary<string, object?>)row, fields);
    }

    public async Task<ReferenceRegisterRecordRead?> SliceLastForEffectiveMomentAsync(
        Guid registerId,
        Guid dimensionSetId,
        DateTime effectiveAsOfUtc,
        DateTime recordedAsOfUtc,
        Guid? recorderDocumentId = null,
        CancellationToken ct = default)
    {
        registerId.EnsureNonEmpty(nameof(registerId));
        // Guid.Empty is a valid DimensionSetId (empty bag)

        effectiveAsOfUtc.EnsureUtc(nameof(effectiveAsOfUtc));
        recordedAsOfUtc.EnsureUtc(nameof(recordedAsOfUtc));
        
        await uow.EnsureConnectionOpenAsync(ct);

        var context = await GetMetadataAsync(registerId, ct);
        var reg = context.Register;

        if (reg.RecordMode == ReferenceRegisterRecordMode.SubordinateToRecorder)
        {
            if (recorderDocumentId is null || recorderDocumentId.Value == Guid.Empty)
                throw new ReferenceRegisterRecordsValidationException(registerId, reason: "recorder_required", details: new { recordMode = reg.RecordMode });
        }
        else
        {
            if (recorderDocumentId is not null)
                throw new ReferenceRegisterRecordsValidationException(registerId, reason: "recorder_forbidden", details: new { recordMode = reg.RecordMode });
        }

        if (reg.Periodicity == ReferenceRegisterPeriodicity.NonPeriodic)
        {
            // Non-periodic has no effective time; treat recordedAsOfUtc as the as-of boundary.
            return await SliceLastAsync(registerId, dimensionSetId, recordedAsOfUtc, recorderDocumentId, ct);
        }

        var table = context.RecordsTable;
        if (!await TableExistsAsync(table, ct))
            return null;

        var fields = context.Fields;

        var fieldsSelect = fields.Count == 0
            ? string.Empty
            : ", " + string.Join(", ", fields.Select(f => $"{f.ColumnCode} AS \"{f.ColumnCode}\""));

        var (wherePeriod, orderByPeriod, bucketEffective) = BuildPeriodClause(reg.Periodicity, effectiveAsOfUtc);

        // BuildPeriodClause uses @AsOfUtc / @BucketAsOfUtc placeholders; adapt to our parameter names.
        wherePeriod = wherePeriod
            .Replace("@AsOfUtc", "@EffectiveAsOfUtc")
            .Replace("@BucketAsOfUtc", "@BucketEffectiveUtc");

        var whereRecorder = reg.RecordMode == ReferenceRegisterRecordMode.SubordinateToRecorder
            ? "AND t.recorder_document_id = @RecorderDocumentId"
            : "AND t.recorder_document_id IS NULL";

        var sql = $"""
                  SELECT
                      record_id            AS "RecordId",
                      dimension_set_id     AS "DimensionSetId",
                      period_utc           AS "PeriodUtc",
                      period_bucket_utc    AS "PeriodBucketUtc",
                      recorder_document_id AS "RecorderDocumentId",
                      recorded_at_utc      AS "RecordedAtUtc",
                      is_deleted           AS "IsDeleted"{fieldsSelect}
                  FROM {table} t
                  WHERE
                      t.dimension_set_id = @DimensionSetId
                      {whereRecorder}
                      AND t.recorded_at_utc <= @RecordedAsOfUtc
                      {wherePeriod}
                  ORDER BY {orderByPeriod} t.recorded_at_utc DESC, t.record_id DESC
                  LIMIT 1;
                  """;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                DimensionSetId = dimensionSetId,
                RecorderDocumentId = recorderDocumentId,
                RecordedAsOfUtc = recordedAsOfUtc,
                EffectiveAsOfUtc = effectiveAsOfUtc,
                BucketEffectiveUtc = bucketEffective,
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = await uow.Connection.QueryAsync(cmd);
        var row = rows.FirstOrDefault();
        if (row is null)
            return null;

        return MapRow((IDictionary<string, object?>)row, fields);
    }

    public Task<IReadOnlyList<ReferenceRegisterRecordRead>> SliceLastAllAsync(
        Guid registerId,
        DateTime asOfUtc,
        Guid? recorderDocumentId = null,
        Guid? afterDimensionSetId = null,
        int limit = 200,
        CancellationToken ct = default)
        => SliceLastAllPageAsync(
            registerId,
            asOfUtc,
            recorderDocumentId,
            afterDimensionSetId,
            limit,
            includeDeleted: true,
            ct);

    public Task<IReadOnlyList<ReferenceRegisterRecordRead>> SliceLastAllPageAsync(
        Guid registerId,
        DateTime asOfUtc,
        Guid? recorderDocumentId = null,
        Guid? afterDimensionSetId = null,
        int limit = 200,
        bool includeDeleted = false,
        CancellationToken ct = default)
        => SliceLastAllPageCoreAsync(
            registerId,
            asOfUtc,
            recorderDocumentId,
            afterDimensionSetId,
            limit,
            includeDeleted,
            visiblePageSize: null,
            ct);

    public Task<IReadOnlyList<ReferenceRegisterRecordRead>> ScanSliceLastAllForVisiblePageAsync(
        Guid registerId,
        DateTime asOfUtc,
        Guid? recorderDocumentId,
        Guid? afterDimensionSetId,
        int pageSize,
        int maxScanPages,
        CancellationToken ct = default)
    {
        var scanLimit = GetRawScanLimit(pageSize, maxScanPages);

        return SliceLastAllPageCoreAsync(
            registerId,
            asOfUtc,
            recorderDocumentId,
            afterDimensionSetId,
            scanLimit,
            includeDeleted: true,
            visiblePageSize: pageSize,
            ct);
    }

    private async Task<IReadOnlyList<ReferenceRegisterRecordRead>> SliceLastAllPageCoreAsync(
        Guid registerId,
        DateTime asOfUtc,
        Guid? recorderDocumentId,
        Guid? afterDimensionSetId,
        int limit,
        bool includeDeleted,
        int? visiblePageSize,
        CancellationToken ct)
    {
        registerId.EnsureNonEmpty(nameof(registerId));
        asOfUtc.EnsureUtc(nameof(asOfUtc));
        
        if (limit < 1)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be >= 1");

        await uow.EnsureConnectionOpenAsync(ct);

        var context = await GetMetadataAsync(registerId, ct);
        var reg = context.Register;

        if (reg.RecordMode == ReferenceRegisterRecordMode.SubordinateToRecorder)
        {
            if (recorderDocumentId is null || recorderDocumentId.Value == Guid.Empty)
                throw new ReferenceRegisterRecordsValidationException(registerId, reason: "recorder_required", details: new { recordMode = reg.RecordMode });
        }
        else
        {
            if (recorderDocumentId is not null)
                throw new ReferenceRegisterRecordsValidationException(registerId, reason: "recorder_forbidden", details: new { recordMode = reg.RecordMode });
        }

        var table = context.RecordsTable;
        if (!await TableExistsAsync(table, ct))
            return [];

        var fields = context.Fields;

        var fieldsSelect = fields.Count == 0
            ? string.Empty
            : ", " + string.Join(", ", fields.Select(f => $"{f.ColumnCode} AS \"{f.ColumnCode}\""));

        var (wherePeriod, orderByPeriod, bucketAsOf) = BuildPeriodClause(reg.Periodicity, asOfUtc);

        var whereRecorder = reg.RecordMode == ReferenceRegisterRecordMode.SubordinateToRecorder
            ? "AND t.recorder_document_id = @RecorderDocumentId"
            : "AND t.recorder_document_id IS NULL";

        var whereAfter = afterDimensionSetId is null
            ? string.Empty
            : "AND t.dimension_set_id > @AfterDimensionSetId";

        // Filter tombstones only after DISTINCT ON has selected the latest version;
        // filtering them in the inner WHERE would resurrect an older active version.
        var resultSql = visiblePageSize is null
            ? """
              SELECT *
                FROM last_rows
               WHERE @IncludeDeleted OR "IsDeleted" = FALSE
               ORDER BY "DimensionSetId", "RecorderDocumentId"
               LIMIT @Limit
              """
            : """
              , numbered_rows AS (
                  SELECT
                      last_rows.*,
                      ROW_NUMBER() OVER (
                          ORDER BY "DimensionSetId", "RecorderDocumentId"
                      ) AS "__ScanPosition",
                      COUNT(*) FILTER (WHERE "IsDeleted" = FALSE) OVER (
                          ORDER BY "DimensionSetId", "RecorderDocumentId"
                          ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
                      ) AS "__VisibleCount"
                  FROM last_rows
              )
              SELECT *
                FROM numbered_rows
               WHERE "__ScanPosition" <= LEAST(
                   CAST(@Limit AS bigint),
                   COALESCE(
                       (
                           SELECT CAST(
                               CEIL(MIN(candidate."__ScanPosition")::numeric / @VisiblePageSize)
                               * @VisiblePageSize
                               AS bigint)
                             FROM numbered_rows candidate
                            WHERE candidate."__VisibleCount" >= @VisiblePageSize
                       ),
                       CAST(@Limit AS bigint)
                   )
               )
               ORDER BY "DimensionSetId", "RecorderDocumentId"
              """;

        var sql = $"""
                  WITH last_rows AS (
                      SELECT DISTINCT ON (t.dimension_set_id, t.recorder_document_id)
                          record_id            AS "RecordId",
                          dimension_set_id     AS "DimensionSetId",
                          period_utc           AS "PeriodUtc",
                          period_bucket_utc    AS "PeriodBucketUtc",
                          recorder_document_id AS "RecorderDocumentId",
                          recorded_at_utc      AS "RecordedAtUtc",
                          is_deleted           AS "IsDeleted"{fieldsSelect}
                      FROM {table} t
                      WHERE
                          t.recorded_at_utc <= @AsOfUtc
                          {whereRecorder}
                          {whereAfter}
                          {wherePeriod}
                      ORDER BY
                          t.dimension_set_id,
                          t.recorder_document_id,
                          {orderByPeriod} t.recorded_at_utc DESC,
                          t.record_id DESC
                  )
                  {resultSql};
                  """;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                AsOfUtc = asOfUtc,
                BucketAsOfUtc = bucketAsOf,
                RecorderDocumentId = recorderDocumentId,
                AfterDimensionSetId = afterDimensionSetId,
                IncludeDeleted = includeDeleted,
                VisiblePageSize = visiblePageSize,
                Limit = limit
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = await uow.Connection.QueryAsync(cmd);
        return MapRows(rows, fields);
    }

    public Task<IReadOnlyList<ReferenceRegisterRecordRead>> SliceLastAllFilteredByDimensionsAsync(
        Guid registerId,
        DateTime asOfUtc,
        IReadOnlyList<DimensionValue> requiredDimensions,
        Guid? recorderDocumentId = null,
        Guid? afterDimensionSetId = null,
        int limit = 200,
        CancellationToken ct = default)
        => SliceLastAllFilteredPageByDimensionsAsync(
            registerId,
            asOfUtc,
            requiredDimensions,
            recorderDocumentId,
            afterDimensionSetId,
            limit,
            includeDeleted: true,
            ct);

    public Task<IReadOnlyList<ReferenceRegisterRecordRead>> SliceLastAllFilteredPageByDimensionsAsync(
        Guid registerId,
        DateTime asOfUtc,
        IReadOnlyList<DimensionValue> requiredDimensions,
        Guid? recorderDocumentId = null,
        Guid? afterDimensionSetId = null,
        int limit = 200,
        bool includeDeleted = false,
        CancellationToken ct = default)
        => SliceLastAllFilteredPageByDimensionsCoreAsync(
            registerId,
            asOfUtc,
            requiredDimensions,
            recorderDocumentId,
            afterDimensionSetId,
            limit,
            includeDeleted,
            visiblePageSize: null,
            ct);

    public Task<IReadOnlyList<ReferenceRegisterRecordRead>> ScanSliceLastAllFilteredForVisiblePageAsync(
        Guid registerId,
        DateTime asOfUtc,
        IReadOnlyList<DimensionValue> requiredDimensions,
        Guid? recorderDocumentId,
        Guid? afterDimensionSetId,
        int pageSize,
        int maxScanPages,
        CancellationToken ct = default)
    {
        var scanLimit = GetRawScanLimit(pageSize, maxScanPages);
        return SliceLastAllFilteredPageByDimensionsCoreAsync(
            registerId,
            asOfUtc,
            requiredDimensions,
            recorderDocumentId,
            afterDimensionSetId,
            scanLimit,
            includeDeleted: true,
            visiblePageSize: pageSize,
            ct);
    }

    private async Task<IReadOnlyList<ReferenceRegisterRecordRead>> SliceLastAllFilteredPageByDimensionsCoreAsync(
        Guid registerId,
        DateTime asOfUtc,
        IReadOnlyList<DimensionValue> requiredDimensions,
        Guid? recorderDocumentId,
        Guid? afterDimensionSetId,
        int limit,
        bool includeDeleted,
        int? visiblePageSize,
        CancellationToken ct)
    {
        registerId.EnsureNonEmpty(nameof(registerId));
        if (requiredDimensions is null)
            throw new NgbArgumentRequiredException(nameof(requiredDimensions));
        
        if (requiredDimensions.Count == 0)
            throw new NgbArgumentOutOfRangeException(nameof(requiredDimensions), requiredDimensions, "RequiredDimensions must be non-empty");

        asOfUtc.EnsureUtc(nameof(asOfUtc));
        
        if (limit < 1)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be >= 1");

        // Ensure unique DimensionId constraints (caller typically passes DimensionBag).
        {
            var seen = new HashSet<Guid>();
            foreach (var dv in requiredDimensions)
            {
                if (!seen.Add(dv.DimensionId))
                    throw new ReferenceRegisterRecordsValidationException(registerId, reason: "duplicate_required_dimension", details: new { dimensionId = dv.DimensionId });
            }
        }

        await uow.EnsureConnectionOpenAsync(ct);

        var context = await GetMetadataAsync(registerId, ct);
        var reg = context.Register;

        if (reg.RecordMode == ReferenceRegisterRecordMode.SubordinateToRecorder)
        {
            if (recorderDocumentId is null || recorderDocumentId.Value == Guid.Empty)
                throw new ReferenceRegisterRecordsValidationException(registerId, reason: "recorder_required", details: new { recordMode = reg.RecordMode });
        }
        else
        {
            if (recorderDocumentId is not null)
                throw new ReferenceRegisterRecordsValidationException(registerId, reason: "recorder_forbidden", details: new { recordMode = reg.RecordMode });
        }

        var table = context.RecordsTable;
        if (!await TableExistsAsync(table, ct))
            return [];

        var fields = context.Fields;

        var fieldsSelect = fields.Count == 0
            ? string.Empty
            : ", " + string.Join(", ", fields.Select(f => $"{f.ColumnCode} AS \"{f.ColumnCode}\""));

        var (wherePeriod, orderByPeriod, bucketAsOf) = BuildPeriodClause(reg.Periodicity, asOfUtc);

        var whereRecorder = reg.RecordMode == ReferenceRegisterRecordMode.SubordinateToRecorder
            ? "AND t.recorder_document_id = @RecorderDocumentId"
            : "AND t.recorder_document_id IS NULL";

        var whereAfter = afterDimensionSetId is null
            ? string.Empty
            : "AND t.dimension_set_id > @AfterDimensionSetId";

        // Build (dimension_id,value_id) predicates for the dimension set filter.
        var dimPredicates = new List<string>(requiredDimensions.Count);
        var p = new DynamicParameters();

        for (var i = 0; i < requiredDimensions.Count; i++)
        {
            var dv = requiredDimensions[i];
            dimPredicates.Add($"(s.dimension_id = @D{i} AND s.value_id = @V{i})");
            p.Add($"D{i}", dv.DimensionId);
            p.Add($"V{i}", dv.ValueId);
        }

        p.Add("DimCount", requiredDimensions.Count);
        p.Add("AsOfUtc", asOfUtc);
        p.Add("BucketAsOfUtc", bucketAsOf);
        p.Add("RecorderDocumentId", recorderDocumentId);
        p.Add("AfterDimensionSetId", afterDimensionSetId);
        p.Add("IncludeDeleted", includeDeleted);
        p.Add("VisiblePageSize", visiblePageSize);
        p.Add("Limit", limit);

        // Filter tombstones only after DISTINCT ON has selected the latest version.
        var resultSql = visiblePageSize is null
            ? """
              SELECT *
                FROM last_rows
               WHERE @IncludeDeleted OR "IsDeleted" = FALSE
               ORDER BY "DimensionSetId", "RecorderDocumentId"
               LIMIT @Limit
              """
            : """
              , numbered_rows AS (
                  SELECT
                      last_rows.*,
                      ROW_NUMBER() OVER (
                          ORDER BY "DimensionSetId", "RecorderDocumentId"
                      ) AS "__ScanPosition",
                      COUNT(*) FILTER (WHERE "IsDeleted" = FALSE) OVER (
                          ORDER BY "DimensionSetId", "RecorderDocumentId"
                          ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
                      ) AS "__VisibleCount"
                  FROM last_rows
              )
              SELECT *
                FROM numbered_rows
               WHERE "__ScanPosition" <= LEAST(
                   CAST(@Limit AS bigint),
                   COALESCE(
                       (
                           SELECT CAST(
                               CEIL(MIN(candidate."__ScanPosition")::numeric / @VisiblePageSize)
                               * @VisiblePageSize
                               AS bigint)
                             FROM numbered_rows candidate
                            WHERE candidate."__VisibleCount" >= @VisiblePageSize
                       ),
                       CAST(@Limit AS bigint)
                   )
               )
               ORDER BY "DimensionSetId", "RecorderDocumentId"
              """;

        var sql = $"""
                  WITH last_rows AS (
                      SELECT DISTINCT ON (t.dimension_set_id, t.recorder_document_id)
                          record_id            AS "RecordId",
                          dimension_set_id     AS "DimensionSetId",
                          period_utc           AS "PeriodUtc",
                          period_bucket_utc    AS "PeriodBucketUtc",
                          recorder_document_id AS "RecorderDocumentId",
                          recorded_at_utc      AS "RecordedAtUtc",
                          is_deleted           AS "IsDeleted"{fieldsSelect}
                      FROM {table} t
                      WHERE
                          t.recorded_at_utc <= @AsOfUtc
                          {whereRecorder}
                          {whereAfter}
                          {wherePeriod}
                          AND t.dimension_set_id IN (
                              SELECT s.dimension_set_id
                              FROM platform_dimension_set_items s
                              WHERE {string.Join(" OR ", dimPredicates)}
                              GROUP BY s.dimension_set_id
                              HAVING COUNT(*) = @DimCount
                          )
                      ORDER BY
                          t.dimension_set_id,
                          t.recorder_document_id,
                          {orderByPeriod} t.recorded_at_utc DESC,
                          t.record_id DESC
                  )
                  {resultSql};
                  """;

        var cmd = new CommandDefinition(
            sql,
            p,
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = await uow.Connection.QueryAsync(cmd);
        return MapRows(rows, fields);
    }

    public async Task<IReadOnlyList<ReferenceRegisterRecordRead>> ListByRecorderDocumentAsync(
        Guid registerId,
        Guid recorderDocumentId,
        DateTime? beforeRecordedAtUtc = null,
        long? beforeRecordId = null,
        int limit = 200,
        CancellationToken ct = default)
    {
        registerId.EnsureNonEmpty(nameof(registerId));
        recorderDocumentId.EnsureNonEmpty(nameof(recorderDocumentId));

        if (beforeRecordedAtUtc is not null)
            beforeRecordedAtUtc.Value.EnsureUtc(nameof(beforeRecordedAtUtc));

        if (beforeRecordedAtUtc is null != beforeRecordId is null)
            throw new NgbArgumentInvalidException("argument", "Cursor must be provided as both BeforeRecordedAtUtc and BeforeRecordId, or neither.");

        if (limit < 1)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be >= 1");

        await uow.EnsureConnectionOpenAsync(ct);

        var context = await GetMetadataAsync(registerId, ct);
        var reg = context.Register;

        if (reg.RecordMode != ReferenceRegisterRecordMode.SubordinateToRecorder)
            return [];

        var table = context.RecordsTable;
        if (!await TableExistsAsync(table, ct))
            return [];

        var fields = context.Fields;

        var fieldsSelect = fields.Count == 0
            ? string.Empty
            : ", " + string.Join(", ", fields.Select(f => $"{f.ColumnCode} AS \"{f.ColumnCode}\""));

        var whereBefore = beforeRecordedAtUtc is null
            ? string.Empty
            : "AND (t.recorded_at_utc, t.record_id) < (@BeforeRecordedAtUtc, @BeforeRecordId)";

        var sql = $"""
                  SELECT
                      record_id            AS "RecordId",
                      dimension_set_id     AS "DimensionSetId",
                      period_utc           AS "PeriodUtc",
                      period_bucket_utc    AS "PeriodBucketUtc",
                      recorder_document_id AS "RecorderDocumentId",
                      recorded_at_utc      AS "RecordedAtUtc",
                      is_deleted           AS "IsDeleted"{fieldsSelect}
                  FROM {table} t
                  WHERE
                      t.recorder_document_id = @RecorderDocumentId
                      {whereBefore}
                  ORDER BY t.recorded_at_utc DESC, t.record_id DESC
                  LIMIT @Limit;
                  """;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                RecorderDocumentId = recorderDocumentId,
                BeforeRecordedAtUtc = beforeRecordedAtUtc,
                BeforeRecordId = beforeRecordId,
                Limit = limit
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = await uow.Connection.QueryAsync(cmd);
        return MapRows(rows, fields);
    }

    public async Task<IReadOnlyList<ReferenceRegisterRecordRead>> ListKeyHistoryAsync(
        Guid registerId,
        Guid dimensionSetId,
        DateTime asOfUtc,
        DateTime? periodUtc = null,
        Guid? recorderDocumentId = null,
        DateTime? beforeRecordedAtUtc = null,
        long? beforeRecordId = null,
        int limit = 200,
        CancellationToken ct = default)
    {
        registerId.EnsureNonEmpty(nameof(registerId));
        // Guid.Empty is a valid DimensionSetId (empty bag)

        asOfUtc.EnsureUtc(nameof(asOfUtc));
        
        if (periodUtc is not null)
            periodUtc.Value.EnsureUtc(nameof(periodUtc));
        
        if (beforeRecordedAtUtc is not null)
            beforeRecordedAtUtc.Value.EnsureUtc(nameof(beforeRecordedAtUtc));
        
        if (beforeRecordedAtUtc is null != beforeRecordId is null)
            throw new NgbArgumentInvalidException("argument", "Cursor must be provided as both BeforeRecordedAtUtc and BeforeRecordId, or neither.");

        if (limit < 1)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be >= 1");

        await uow.EnsureConnectionOpenAsync(ct);

        var context = await GetMetadataAsync(registerId, ct);
        var reg = context.Register;

        if (reg.RecordMode == ReferenceRegisterRecordMode.SubordinateToRecorder)
        {
            if (recorderDocumentId is null || recorderDocumentId.Value == Guid.Empty)
                throw new ReferenceRegisterRecordsValidationException(registerId, reason: "recorder_required", details: new { recordMode = reg.RecordMode });
        }
        else
        {
            if (recorderDocumentId is not null)
                throw new ReferenceRegisterRecordsValidationException(registerId, reason: "recorder_forbidden", details: new { recordMode = reg.RecordMode });
        }

        if (reg.Periodicity == ReferenceRegisterPeriodicity.NonPeriodic)
        {
            if (periodUtc is not null)
                throw new ReferenceRegisterRecordsValidationException(registerId, reason: "period_not_allowed_for_non_periodic", details: new { periodicity = reg.Periodicity, periodUtc });
        }
        else
        {
            if (periodUtc is null)
                throw new ReferenceRegisterRecordsValidationException(registerId, reason: "period_required_for_periodic", details: new { periodicity = reg.Periodicity, periodUtc });
        }

        var table = context.RecordsTable;
        if (!await TableExistsAsync(table, ct))
            return [];

        var fields = context.Fields;

        var fieldsSelect = fields.Count == 0
            ? string.Empty
            : ", " + string.Join(", ", fields.Select(f => $"{f.ColumnCode} AS \"{f.ColumnCode}\""));

        var whereRecorder = reg.RecordMode == ReferenceRegisterRecordMode.SubordinateToRecorder
            ? "AND t.recorder_document_id = @RecorderDocumentId"
            : "AND t.recorder_document_id IS NULL";

        var bucket = reg.Periodicity == ReferenceRegisterPeriodicity.NonPeriodic
            ? null
            : ReferenceRegisterPeriodBucket.ComputeUtc(periodUtc, reg.Periodicity);

        var wherePeriod = reg.Periodicity == ReferenceRegisterPeriodicity.NonPeriodic
            ? string.Empty
            : "AND t.period_bucket_utc = @PeriodBucketUtc";

        var whereBefore = beforeRecordedAtUtc is null
            ? string.Empty
            : "AND (t.recorded_at_utc, t.record_id) < (@BeforeRecordedAtUtc, @BeforeRecordId)";

        var sql = $"""
                  SELECT
                      record_id            AS "RecordId",
                      dimension_set_id     AS "DimensionSetId",
                      period_utc           AS "PeriodUtc",
                      period_bucket_utc    AS "PeriodBucketUtc",
                      recorder_document_id AS "RecorderDocumentId",
                      recorded_at_utc      AS "RecordedAtUtc",
                      is_deleted           AS "IsDeleted"{fieldsSelect}
                  FROM {table} t
                  WHERE
                      t.dimension_set_id = @DimensionSetId
                      {whereRecorder}
                      {wherePeriod}
                      AND t.recorded_at_utc <= @AsOfUtc
                      {whereBefore}
                  ORDER BY t.recorded_at_utc DESC, t.record_id DESC
                  LIMIT @Limit;
                  """;

        var cmd = new CommandDefinition(
            sql,
            new
            {
                DimensionSetId = dimensionSetId,
                RecorderDocumentId = recorderDocumentId,
                PeriodBucketUtc = bucket,
                AsOfUtc = asOfUtc,
                BeforeRecordedAtUtc = beforeRecordedAtUtc,
                BeforeRecordId = beforeRecordId,
                Limit = limit
            },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = await uow.Connection.QueryAsync(cmd);
        return MapRows(rows, fields);
    }

    private async Task<ReferenceRegisterMetadataContext> GetMetadataAsync(Guid registerId, CancellationToken ct)
    {
        if (_localMetadata.TryGetValue(registerId, out var cached))
            return cached;

        var context = await _metadataCache.GetOrCreateAsync(
            registerId,
            loadCt => LoadMetadataAsync(registerId, loadCt),
            ct);
        _localMetadata[registerId] = context;

        return context;
    }

    private async Task<ReferenceRegisterMetadataContext> LoadMetadataAsync(Guid registerId, CancellationToken ct)
    {
        var register = await registers.GetByIdAsync(registerId, ct)
            ?? throw new ReferenceRegisterNotFoundException(registerId);
        var table = ReferenceRegisterNaming.RecordsTable(register.TableCode);

        ReferenceRegisterSqlIdentifiers.EnsureOrThrow(table, "records table");

        var fields = (await fieldsRepo.GetByRegisterIdAsync(registerId, ct))
            .OrderBy(static field => field.Ordinal)
            .ToArray();

        foreach (var field in fields)
        {
            ReferenceRegisterSqlIdentifiers.EnsureOrThrow(field.ColumnCode, "field column_code");
        }

        return new ReferenceRegisterMetadataContext(register, fields, table);
    }

    private Task<bool> TableExistsAsync(string tableName, CancellationToken ct)
        => _relationPresenceCache.ExistsAsync(
            tableName,
            probeCt => PostgresTableExistence.ExistsAsync(uow, tableName, probeCt),
            ct);

    private static int GetRawScanLimit(int pageSize, int maxScanPages)
    {
        if (pageSize < 1)
            throw new NgbArgumentOutOfRangeException(nameof(pageSize), pageSize, "Page size must be >= 1");

        if (maxScanPages < 1)
            throw new NgbArgumentOutOfRangeException(nameof(maxScanPages), maxScanPages, "Maximum scan pages must be >= 1");

        return pageSize > int.MaxValue / maxScanPages
            ? int.MaxValue
            : pageSize * maxScanPages;
    }

    private static IReadOnlyList<ReferenceRegisterRecordRead> MapRows(
        IEnumerable<dynamic> rows,
        IReadOnlyList<ReferenceRegisterField> fields)
    {
        var result = new List<ReferenceRegisterRecordRead>();

        foreach (var row in rows)
        {
            result.Add(MapRow((IDictionary<string, object?>)row, fields));
        }

        return result;
    }

    internal static ReferenceRegisterRecordRead MapRow(
        IDictionary<string, object?> data,
        IReadOnlyList<ReferenceRegisterField> fields)
    {
        static DateTime? OptionalDateTime(object? value) => value is null or DBNull ? null : (DateTime)value;

        static Guid? OptionalGuid(object? value) => value is null or DBNull ? null : (Guid)value;

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            var value = data.TryGetValue(field.ColumnCode, out var raw) ? raw : null;
            values[field.CodeNorm] = value is DBNull ? null : value;
        }

        return new ReferenceRegisterRecordRead(
            Convert.ToInt64(data["RecordId"]!),
            (Guid)data["DimensionSetId"]!,
            OptionalDateTime(data["PeriodUtc"]),
            OptionalDateTime(data["PeriodBucketUtc"]),
            OptionalGuid(data["RecorderDocumentId"]),
            (DateTime)data["RecordedAtUtc"]!,
            (bool)data["IsDeleted"]!,
            values);
    }

    private static (string WherePeriod, string OrderByPeriod, DateTime? BucketAsOfUtc) BuildPeriodClause(
        ReferenceRegisterPeriodicity periodicity,
        DateTime asOfUtc)
    {
        if (periodicity == ReferenceRegisterPeriodicity.NonPeriodic)
            return (WherePeriod: string.Empty, OrderByPeriod: string.Empty, BucketAsOfUtc: null);

        var bucket = ReferenceRegisterPeriodBucket.ComputeUtc(asOfUtc, periodicity);

        // SliceLast across buckets: we want the most recent period <= AsOfUtc.
        // Order by bucket first for index friendliness.
        var where = "AND t.period_utc <= @AsOfUtc AND t.period_bucket_utc <= @BucketAsOfUtc";
        var orderBy = "t.period_bucket_utc DESC, t.period_utc DESC, ";

        return (where, orderBy, bucket);
    }
}
