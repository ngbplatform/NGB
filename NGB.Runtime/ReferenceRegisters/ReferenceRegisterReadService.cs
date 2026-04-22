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
            var list = await recordsReader.SliceLastAllAsync(
                registerId,
                asOfUtc,
                recorderDocumentId,
                afterDimensionSetId,
                limit,
                ct);

            return new ReferenceRegisterSlicePage<ReferenceRegisterRecordRead>(
                Records: list,
                NextAfterDimensionSetId: list.Count == 0 ? afterDimensionSetId : list[^1].DimensionSetId,
                HasMore: list.Count == limit);
        }

        // If many keys are tombstoned, a single persistence page might return mostly deleted rows
        // and UI would see short pages. We "skip" tombstones by fetching subsequent key pages
        // until we fill the requested visible limit (or the keyspace ends). Cursor-aware variant
        // returns the next key cursor advanced by the last *examined* key.
        return await SliceLastAllVisiblePageSkippingTombstonesAsync(
            registerId,
            asOfUtc,
            recorderDocumentId,
            afterDimensionSetId,
            limit,
            ct);
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
            var list = await recordsReader.SliceLastAllFilteredByDimensionsAsync(
                registerId,
                asOfUtc,
                bag.Items,
                recorderDocumentId,
                afterDimensionSetId,
                limit,
                ct);

            return new ReferenceRegisterSlicePage<ReferenceRegisterRecordRead>(
                Records: list,
                NextAfterDimensionSetId: list.Count == 0 ? afterDimensionSetId : list[^1].DimensionSetId,
                HasMore: list.Count == limit);
        }

        return await SliceLastAllVisiblePageSkippingTombstonesAsync(
            registerId,
            asOfUtc,
            bag.Items,
            recorderDocumentId,
            afterDimensionSetId,
            limit,
            ct);
    }

    private async Task<ReferenceRegisterSlicePage<ReferenceRegisterRecordRead>> SliceLastAllVisiblePageSkippingTombstonesAsync(
        Guid registerId,
        DateTime asOfUtc,
        Guid? recorderDocumentId,
        Guid? afterDimensionSetId,
        int limit,
        CancellationToken ct)
    {
        var result = new List<ReferenceRegisterRecordRead>(capacity: limit);

        Guid? cursor = afterDimensionSetId;

        var hasMore = false;

        for (var i = 0; i < MaxTombstoneSkipIterations && result.Count < limit; i++)
        {
            var page = await recordsReader.SliceLastAllAsync(
                registerId,
                asOfUtc,
                recorderDocumentId,
                cursor,
                limit,
                ct);

            if (page.Count == 0)
                return new ReferenceRegisterSlicePage<ReferenceRegisterRecordRead>(
                    Records: result,
                    NextAfterDimensionSetId: cursor,
                    HasMore: false);

            hasMore = page.Count == limit;

            foreach (var r in page)
            {
                if (!r.IsDeleted)
                {
                    result.Add(r);
                    if (result.Count == limit)
                        break;
                }
            }

            // Advance key cursor by last seen key.
            cursor = page[^1].DimensionSetId;

            // End of keyspace.
            if (page.Count < limit)
                return new ReferenceRegisterSlicePage<ReferenceRegisterRecordRead>(
                    Records: result,
                    NextAfterDimensionSetId: cursor,
                    HasMore: false);
        }

        return new ReferenceRegisterSlicePage<ReferenceRegisterRecordRead>(
            Records: result,
            NextAfterDimensionSetId: cursor,
            HasMore: hasMore);
    }

    private async Task<ReferenceRegisterSlicePage<ReferenceRegisterRecordRead>> SliceLastAllVisiblePageSkippingTombstonesAsync(
        Guid registerId,
        DateTime asOfUtc,
        IReadOnlyList<DimensionValue> requiredDimensions,
        Guid? recorderDocumentId,
        Guid? afterDimensionSetId,
        int limit,
        CancellationToken ct)
    {
        var result = new List<ReferenceRegisterRecordRead>(capacity: limit);

        Guid? cursor = afterDimensionSetId;

        var hasMore = false;

        for (var i = 0; i < MaxTombstoneSkipIterations && result.Count < limit; i++)
        {
            var page = await recordsReader.SliceLastAllFilteredByDimensionsAsync(
                registerId,
                asOfUtc,
                requiredDimensions,
                recorderDocumentId,
                cursor,
                limit,
                ct);

            if (page.Count == 0)
                return new ReferenceRegisterSlicePage<ReferenceRegisterRecordRead>(
                    Records: result,
                    NextAfterDimensionSetId: cursor,
                    HasMore: false);

            hasMore = page.Count == limit;

            foreach (var r in page)
            {
                if (!r.IsDeleted)
                {
                    result.Add(r);
                    if (result.Count == limit)
                        break;
                }
            }

            cursor = page[^1].DimensionSetId;

            if (page.Count < limit)
                return new ReferenceRegisterSlicePage<ReferenceRegisterRecordRead>(
                    Records: result,
                    NextAfterDimensionSetId: cursor,
                    HasMore: false);
        }

        return new ReferenceRegisterSlicePage<ReferenceRegisterRecordRead>(
            Records: result,
            NextAfterDimensionSetId: cursor,
            HasMore: hasMore);
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
        if (records.Count == 0)
            return [];

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
                    byDim[dv.DimensionId] = s.Length > 8 ? s[..8] : s;
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
