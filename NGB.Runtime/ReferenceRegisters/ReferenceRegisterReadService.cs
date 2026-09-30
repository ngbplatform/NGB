using NGB.Core.Dimensions;
using NGB.Core.Dimensions.Enrichment;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Dimensions.Enrichment;
using NGB.Persistence.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.Runtime.ReferenceRegisters;

public sealed class ReferenceRegisterReadService(
    IReferenceRegisterRecordsReader recordsReader,
    IDimensionSetReader dimensionSetReader,
    IDimensionValueEnrichmentReader dimensionValueEnrichmentReader)
    : IReferenceRegisterReadService
{
    private const int MaxTombstoneSkipIterations = 25;

    public Task<ReferenceRegisterRecordRead?> SliceLastAsync(
        Guid registerId,
        IReadOnlyList<DimensionValue>? dimensions,
        DateTime asOfUtc,
        Guid? recorderDocumentId = null,
        bool includeDeleted = false,
        CancellationToken ct = default)
    {
        var bag = dimensions is { Count: > 0 }
            ? new DimensionBag(dimensions)
            : DimensionBag.Empty;

        var dimensionSetId = DeterministicDimensionSetId.FromBag(bag);

        return SliceLastByDimensionSetIdAsync(
            registerId,
            dimensionSetId,
            asOfUtc,
            recorderDocumentId,
            includeDeleted,
            ct);
    }

    public async Task<ReferenceRegisterRecordRead?> SliceLastByDimensionSetIdAsync(
        Guid registerId,
        Guid dimensionSetId,
        DateTime asOfUtc,
        Guid? recorderDocumentId = null,
        bool includeDeleted = false,
        CancellationToken ct = default)
    {
        registerId.EnsureRequired(nameof(registerId));
        // Guid.Empty is a valid DimensionSetId (empty bag)

        asOfUtc.EnsureUtc(nameof(asOfUtc));

        var record = await recordsReader.SliceLastAsync(
            registerId,
            dimensionSetId,
            asOfUtc,
            recorderDocumentId,
            ct);

        if (record is null)
            return null;

        if (!includeDeleted && record.IsDeleted)
            return null;

        return record;
    }

    public async Task<IReadOnlyList<ReferenceRegisterRecordRead>> SliceLastAllAsync(
        Guid registerId,
        DateTime asOfUtc,
        Guid? recorderDocumentId = null,
        Guid? afterDimensionSetId = null,
        int limit = 200,
        bool includeDeleted = false,
        CancellationToken ct = default)
    {
        registerId.EnsureRequired(nameof(registerId));
        asOfUtc.EnsureUtc(nameof(asOfUtc));

        if (limit < 1)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be greater than 0.");

        var page = await SliceLastAllPageAsync(
            registerId,
            asOfUtc,
            recorderDocumentId,
            afterDimensionSetId,
            limit,
            includeDeleted,
            ct);

        return page.Records;
    }

    public async Task<ReferenceRegisterSlicePage<ReferenceRegisterRecordRead>> SliceLastAllPageAsync(
        Guid registerId,
        DateTime asOfUtc,
        Guid? recorderDocumentId = null,
        Guid? afterDimensionSetId = null,
        int limit = 200,
        bool includeDeleted = false,
        CancellationToken ct = default)
    {
        registerId.EnsureRequired(nameof(registerId));
        asOfUtc.EnsureUtc(nameof(asOfUtc));

        if (limit < 1)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be greater than 0.");

        if (includeDeleted)
        {
            var list = await recordsReader.SliceLastAllPageAsync(
                registerId,
                asOfUtc,
                recorderDocumentId,
                afterDimensionSetId,
                limit,
                includeDeleted: true,
                ct);

            return CreatePage(list, afterDimensionSetId, limit);
        }

        // Preserve the public cursor contract (advance by the last examined key),
        // while avoiding the former N-query tombstone skipping loop. Persistence
        // returns at most 25 raw pages in one round-trip and we replay the paging
        // semantics in memory.
        var scanLimit = GetTombstoneScanLimit(limit);
        var rawRecords = await recordsReader.ScanSliceLastAllForVisiblePageAsync(
            registerId,
            asOfUtc,
            recorderDocumentId,
            afterDimensionSetId,
            limit,
            MaxTombstoneSkipIterations,
            ct);

        return CreateVisiblePageFromRawRecords(rawRecords, afterDimensionSetId, limit, scanLimit);
    }

    public async Task<IReadOnlyList<ReferenceRegisterRecordRead>> SliceLastAllFilteredAsync(
        Guid registerId,
        DateTime asOfUtc,
        IReadOnlyList<DimensionValue>? requiredDimensions,
        Guid? recorderDocumentId = null,
        Guid? afterDimensionSetId = null,
        int limit = 200,
        bool includeDeleted = false,
        CancellationToken ct = default)
    {
        var page = await SliceLastAllFilteredPageAsync(
            registerId,
            asOfUtc,
            requiredDimensions,
            recorderDocumentId,
            afterDimensionSetId,
            limit,
            includeDeleted,
            ct);

        return page.Records;
    }

    public async Task<ReferenceRegisterSlicePage<ReferenceRegisterRecordRead>> SliceLastAllFilteredPageAsync(
        Guid registerId,
        DateTime asOfUtc,
        IReadOnlyList<DimensionValue>? requiredDimensions,
        Guid? recorderDocumentId = null,
        Guid? afterDimensionSetId = null,
        int limit = 200,
        bool includeDeleted = false,
        CancellationToken ct = default)
    {
        if (requiredDimensions is null || requiredDimensions.Count == 0)
        {
            return await SliceLastAllPageAsync(
                registerId,
                asOfUtc,
                recorderDocumentId,
                afterDimensionSetId,
                limit,
                includeDeleted,
                ct);
        }

        // Canonicalize and validate uniqueness.
        var bag = new DimensionBag(requiredDimensions);

        registerId.EnsureRequired(nameof(registerId));
        asOfUtc.EnsureUtc(nameof(asOfUtc));

        if (limit < 1)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be greater than 0.");

        if (includeDeleted)
        {
            var list = await recordsReader.SliceLastAllFilteredPageByDimensionsAsync(
                registerId,
                asOfUtc,
                bag.Items,
                recorderDocumentId,
                afterDimensionSetId,
                limit,
                includeDeleted: true,
                ct);

            return CreatePage(list, afterDimensionSetId, limit);
        }

        var scanLimit = GetTombstoneScanLimit(limit);
        var rawRecords = await recordsReader.ScanSliceLastAllFilteredForVisiblePageAsync(
            registerId,
            asOfUtc,
            bag.Items,
            recorderDocumentId,
            afterDimensionSetId,
            limit,
            MaxTombstoneSkipIterations,
            ct);

        return CreateVisiblePageFromRawRecords(rawRecords, afterDimensionSetId, limit, scanLimit);
    }

    private static int GetTombstoneScanLimit(int pageSize) =>
        pageSize > int.MaxValue / MaxTombstoneSkipIterations
            ? int.MaxValue
            : pageSize * MaxTombstoneSkipIterations;

    private static ReferenceRegisterSlicePage<ReferenceRegisterRecordRead> CreatePage(
        IReadOnlyList<ReferenceRegisterRecordRead> records,
        Guid? afterDimensionSetId,
        int limit) =>
        new(
            Records: records,
            NextAfterDimensionSetId: records.Count == 0 ? afterDimensionSetId : records[^1].DimensionSetId,
            HasMore: records.Count == limit);

    private static ReferenceRegisterSlicePage<ReferenceRegisterRecordRead> CreateVisiblePageFromRawRecords(
        IReadOnlyList<ReferenceRegisterRecordRead> rawRecords,
        Guid? afterDimensionSetId,
        int pageSize,
        int scanLimit)
    {
        if (rawRecords.Count == 0)
            return new ReferenceRegisterSlicePage<ReferenceRegisterRecordRead>([], afterDimensionSetId, false);

        var visible = new List<ReferenceRegisterRecordRead>(Math.Min(pageSize, rawRecords.Count));
        var cursor = afterDimensionSetId;

        for (var offset = 0; offset < rawRecords.Count; offset += pageSize)
        {
            var rawPageCount = Math.Min(pageSize, rawRecords.Count - offset);

            for (var index = 0; index < rawPageCount && visible.Count < pageSize; index++)
            {
                var record = rawRecords[offset + index];
                if (!record.IsDeleted)
                    visible.Add(record);
            }

            // The legacy contract advances past the complete persistence page,
            // including tombstones and visible rows not returned after the limit.
            cursor = rawRecords[offset + rawPageCount - 1].DimensionSetId;

            if (visible.Count == pageSize)
            {
                return new ReferenceRegisterSlicePage<ReferenceRegisterRecordRead>(
                    visible,
                    cursor,
                    HasMore: rawPageCount == pageSize);
            }

            if (rawPageCount < pageSize)
                return new ReferenceRegisterSlicePage<ReferenceRegisterRecordRead>(visible, cursor, false);
        }

        // If the single over-fetch exhausted the underlying keyspace before the
        // safety cap, the old implementation would perform one final empty read.
        var reachedSafetyCap = rawRecords.Count == scanLimit;
        return new ReferenceRegisterSlicePage<ReferenceRegisterRecordRead>(visible, cursor, reachedSafetyCap);
    }

    public async Task<IReadOnlyList<ReferenceRegisterRecordSnapshot>> SliceLastAllEnrichedAsync(
        Guid registerId,
        DateTime asOfUtc,
        IReadOnlyList<DimensionValue>? requiredDimensions = null,
        Guid? recorderDocumentId = null,
        Guid? afterDimensionSetId = null,
        int limit = 200,
        bool includeDeleted = false,
        CancellationToken ct = default)
    {
        var page = await SliceLastAllEnrichedPageAsync(
            registerId,
            asOfUtc,
            requiredDimensions,
            recorderDocumentId,
            afterDimensionSetId,
            limit,
            includeDeleted,
            ct);

        return page.Records;
    }

    public async Task<ReferenceRegisterSlicePage<ReferenceRegisterRecordSnapshot>> SliceLastAllEnrichedPageAsync(
        Guid registerId,
        DateTime asOfUtc,
        IReadOnlyList<DimensionValue>? requiredDimensions = null,
        Guid? recorderDocumentId = null,
        Guid? afterDimensionSetId = null,
        int limit = 200,
        bool includeDeleted = false,
        CancellationToken ct = default)
    {
        var recordsPage = await SliceLastAllFilteredPageAsync(
            registerId,
            asOfUtc,
            requiredDimensions,
            recorderDocumentId,
            afterDimensionSetId,
            limit,
            includeDeleted,
            ct);

        if (recordsPage.Records.Count == 0)
        {
            return new ReferenceRegisterSlicePage<ReferenceRegisterRecordSnapshot>(
                Records: [],
                NextAfterDimensionSetId: recordsPage.NextAfterDimensionSetId,
                HasMore: recordsPage.HasMore);
        }

        var snapshots = await EnrichAsync(recordsPage.Records, ct);

        return new ReferenceRegisterSlicePage<ReferenceRegisterRecordSnapshot>(
            Records: snapshots,
            NextAfterDimensionSetId: recordsPage.NextAfterDimensionSetId,
            HasMore: recordsPage.HasMore);
    }

    private async Task<IReadOnlyList<ReferenceRegisterRecordSnapshot>> EnrichAsync(
        IReadOnlyList<ReferenceRegisterRecordRead> records,
        CancellationToken ct)
    {
        var setIds = records.Select(x => x.DimensionSetId).Distinct().ToArray();
        var bagsById = await dimensionSetReader.GetBagsByIdsAsync(setIds, ct);

        var keys = new HashSet<DimensionValueKey>();
        foreach (var id in setIds)
        {
            if (!bagsById.TryGetValue(id, out var bag))
                continue;

            foreach (var dv in bag.Items)
                keys.Add(new DimensionValueKey(dv.DimensionId, dv.ValueId));
        }

        var displayByKey = keys.Count == 0
            ? new Dictionary<DimensionValueKey, string>()
            : (await dimensionValueEnrichmentReader.ResolveAsync(keys, ct)).ToDictionary(kv => kv.Key, kv => kv.Value);

        var list = new List<ReferenceRegisterRecordSnapshot>(capacity: records.Count);

        foreach (var r in records)
        {
            var bag = bagsById.GetValueOrDefault(r.DimensionSetId, DimensionBag.Empty);
            var byDim = new Dictionary<Guid, string>(capacity: bag.Items.Count);

            foreach (var dv in bag.Items)
            {
                var k = new DimensionValueKey(dv.DimensionId, dv.ValueId);
                if (displayByKey.TryGetValue(k, out var display))
                {
                    byDim[dv.DimensionId] = display;
                }
                else
                {
                    var s = dv.ValueId.ToString("N");
                    byDim[dv.DimensionId] = s[..8];
                }
            }

            list.Add(new ReferenceRegisterRecordSnapshot(r, bag, byDim));
        }

        return list;
    }

    public Task<IReadOnlyList<ReferenceRegisterRecordRead>> GetKeyHistoryAsync(
        Guid registerId,
        IReadOnlyList<DimensionValue>? dimensions,
        DateTime asOfUtc,
        DateTime? periodUtc = null,
        Guid? recorderDocumentId = null,
        DateTime? beforeRecordedAtUtc = null,
        long? beforeRecordId = null,
        int limit = 200,
        bool includeDeleted = false,
        CancellationToken ct = default)
    {
        var bag = dimensions is { Count: > 0 }
            ? new DimensionBag(dimensions)
            : DimensionBag.Empty;

        var dimensionSetId = DeterministicDimensionSetId.FromBag(bag);

        return GetKeyHistoryByDimensionSetIdAsync(
            registerId,
            dimensionSetId,
            asOfUtc,
            periodUtc,
            recorderDocumentId,
            beforeRecordedAtUtc,
            beforeRecordId,
            limit,
            includeDeleted,
            ct);
    }

    public async Task<IReadOnlyList<ReferenceRegisterRecordRead>> GetKeyHistoryByDimensionSetIdAsync(
        Guid registerId,
        Guid dimensionSetId,
        DateTime asOfUtc,
        DateTime? periodUtc = null,
        Guid? recorderDocumentId = null,
        DateTime? beforeRecordedAtUtc = null,
        long? beforeRecordId = null,
        int limit = 200,
        bool includeDeleted = false,
        CancellationToken ct = default)
    {
        registerId.EnsureRequired(nameof(registerId));
        asOfUtc.EnsureUtc(nameof(asOfUtc));

        if (periodUtc is not null)
            periodUtc.Value.EnsureUtc(nameof(periodUtc));

        if (beforeRecordedAtUtc is not null)
            beforeRecordedAtUtc.Value.EnsureUtc(nameof(beforeRecordedAtUtc));

        if ((beforeRecordedAtUtc is null) != (beforeRecordId is null))
            throw new NgbArgumentInvalidException("cursor", "Cursor must be provided as both beforeRecordedAtUtc and beforeRecordId, or neither.");

        if (limit < 1)
            throw new NgbArgumentOutOfRangeException(nameof(limit), limit, "Limit must be greater than 0.");

        var records = await recordsReader.ListKeyHistoryAsync(
            registerId,
            dimensionSetId,
            asOfUtc,
            periodUtc,
            recorderDocumentId,
            beforeRecordedAtUtc,
            beforeRecordId,
            limit,
            ct);

        if (includeDeleted)
            return records;

        return records.Where(x => !x.IsDeleted).ToArray();
    }
}
