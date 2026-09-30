using FluentAssertions;
using Moq;
using NGB.Core.Dimensions;
using NGB.Core.Dimensions.Enrichment;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Dimensions.Enrichment;
using NGB.Persistence.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.ReferenceRegisters;

public sealed class ReferenceRegisterReadFullCoverageTests
{
    private static readonly DateTime Now = new(2026, 8, 15, 21, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task SliceLast_CanonicalizesDimensionsValidatesAndHandlesNullDeletedAndVisibleRows()
    {
        var f = new Fixture();
        var id = Guid.NewGuid();
        var dimension = new DimensionValue(Guid.NewGuid(), Guid.NewGuid());
        await ((Func<Task>)(() => f.Sut.SliceLastByDimensionSetIdAsync(Guid.Empty, Guid.Empty, Now)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.SliceLastByDimensionSetIdAsync(id, Guid.Empty, LocalTime())))
            .Should().ThrowAsync<NgbArgumentInvalidException>();

        f.Reader.SetupSequence(x => x.SliceLastAsync(id, It.IsAny<Guid>(), Now, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReferenceRegisterRecordRead?)null)
            .ReturnsAsync(Record(Guid.Empty, deleted: true))
            .ReturnsAsync(Record(Guid.Empty, deleted: true))
            .ReturnsAsync(Record(Guid.Empty));
        (await f.Sut.SliceLastAsync(id, null, Now)).Should().BeNull();
        (await f.Sut.SliceLastAsync(id, [], Now)).Should().BeNull();
        (await f.Sut.SliceLastAsync(id, [dimension, dimension], Now, includeDeleted: true))!.IsDeleted.Should().BeTrue();
        (await f.Sut.SliceLastByDimensionSetIdAsync(id, Guid.Empty, Now))!.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task SliceAll_ValidatesAndIncludeDeletedCoversCursorAndHasMoreShapes()
    {
        var f = new Fixture();
        var id = Guid.NewGuid();
        var cursor = Guid.NewGuid();
        await ((Func<Task>)(() => f.Sut.SliceLastAllAsync(Guid.Empty, Now))).Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.SliceLastAllAsync(id, LocalTime()))).Should().ThrowAsync<NgbArgumentInvalidException>();
        await ((Func<Task>)(() => f.Sut.SliceLastAllAsync(id, Now, limit: 0))).Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => f.Sut.SliceLastAllPageAsync(id, Now, limit: 0))).Should().ThrowAsync<NgbArgumentOutOfRangeException>();

        f.Reader.SetupSequence(x => x.SliceLastAllPageAsync(
                id, Now, null, cursor, 2, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .ReturnsAsync([Record(Guid.NewGuid()), Record(Guid.NewGuid())]);
        var empty = await f.Sut.SliceLastAllPageAsync(id, Now, afterDimensionSetId: cursor,
            limit: 2, includeDeleted: true);
        empty.NextAfterDimensionSetId.Should().Be(cursor);
        empty.HasMore.Should().BeFalse();
        var full = await f.Sut.SliceLastAllPageAsync(id, Now, afterDimensionSetId: cursor,
            limit: 2, includeDeleted: true);
        full.Records.Should().HaveCount(2);
        full.NextAfterDimensionSetId.Should().Be(full.Records[^1].DimensionSetId);
        full.HasMore.Should().BeTrue();

        f.Reader.Setup(x => x.SliceLastAllPageAsync(
                id, Now, null, null, 1, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Record(Guid.NewGuid())]);
        (await f.Sut.SliceLastAllAsync(id, Now, limit: 1, includeDeleted: true)).Should().ContainSingle();
    }

    [Fact]
    public async Task SliceAllVisible_UsesSingleRawScanAndPreservesPageMetadata()
    {
        var id = Guid.NewGuid();

        var emptyFixture = new Fixture();
        var cursor = Guid.NewGuid();
        emptyFixture.Reader.Setup(x => x.ScanSliceLastAllForVisiblePageAsync(
                id, Now, null, cursor, 2, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var empty = await emptyFixture.Sut.SliceLastAllPageAsync(id, Now, afterDimensionSetId: cursor, limit: 2);
        empty.Records.Should().BeEmpty();
        empty.NextAfterDimensionSetId.Should().Be(cursor);
        empty.HasMore.Should().BeFalse();

        var fill = new Fixture();
        var visible1 = Guid.NewGuid();
        var visible2 = Guid.NewGuid();
        fill.Reader.Setup(x => x.ScanSliceLastAllForVisiblePageAsync(
                id, Now, null, null, 2, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Record(visible1), Record(visible2)]);
        var filled = await fill.Sut.SliceLastAllPageAsync(id, Now, limit: 2);
        filled.Records.Select(x => x.DimensionSetId).Should().Equal(visible1, visible2);
        filled.HasMore.Should().BeTrue();

        var shortPage = new Fixture();
        var last = Guid.NewGuid();
        shortPage.Reader.Setup(x => x.ScanSliceLastAllForVisiblePageAsync(
                id, Now, null, null, 3, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Record(last)]);
        var shortResult = await shortPage.Sut.SliceLastAllPageAsync(id, Now, limit: 3);
        shortResult.Records.Should().ContainSingle();
        shortResult.NextAfterDimensionSetId.Should().Be(last);
        shortResult.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task SliceAllVisible_EmptyVisibleResultRequiresOnlyOnePersistenceQuery()
    {
        var f = new Fixture();
        var id = Guid.NewGuid();
        f.Reader.Setup(x => x.ScanSliceLastAllForVisiblePageAsync(
                id, Now, null, null, 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var page = await f.Sut.SliceLastAllPageAsync(id, Now, limit: 1);

        page.Records.Should().BeEmpty();
        page.HasMore.Should().BeFalse();
        f.Reader.Verify(x => x.ScanSliceLastAllForVisiblePageAsync(
            id, Now, null, null, 1, 25, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SliceAllVisible_AdvancesCursorByLastExaminedRawPageWhenSkippingTombstones()
    {
        var f = new Fixture();
        var id = Guid.NewGuid();
        var tombstone1 = Guid.NewGuid();
        var visible1 = Guid.NewGuid();
        var visible2 = Guid.NewGuid();
        var examinedAfterLimit = Guid.NewGuid();
        f.Reader.Setup(x => x.ScanSliceLastAllForVisiblePageAsync(
                id, Now, null, null, 2, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Record(tombstone1, deleted: true),
                Record(visible1),
                Record(visible2),
                Record(examinedAfterLimit)
            ]);

        var page = await f.Sut.SliceLastAllPageAsync(id, Now, limit: 2);

        page.Records.Select(static x => x.DimensionSetId).Should().Equal(visible1, visible2);
        page.NextAfterDimensionSetId.Should().Be(examinedAfterLimit);
        page.HasMore.Should().BeTrue();
        f.Reader.VerifyAll();
    }

    [Fact]
    public async Task SliceAllVisible_CoversExhaustedAndSafetyCappedTombstoneScans()
    {
        var id = Guid.NewGuid();

        var exhausted = new Fixture();
        var exhaustedRows = new[] { Record(Guid.NewGuid(), true), Record(Guid.NewGuid(), true) };
        exhausted.Reader.Setup(x => x.ScanSliceLastAllForVisiblePageAsync(
                id, Now, null, null, 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(exhaustedRows);
        var exhaustedPage = await exhausted.Sut.SliceLastAllPageAsync(id, Now, limit: 1);
        exhaustedPage.Records.Should().BeEmpty();
        exhaustedPage.NextAfterDimensionSetId.Should().Be(exhaustedRows[^1].DimensionSetId);
        exhaustedPage.HasMore.Should().BeFalse();

        var capped = new Fixture();
        var cappedRows = Enumerable.Range(0, 25)
            .Select(_ => Record(Guid.NewGuid(), true))
            .ToArray();
        capped.Reader.Setup(x => x.ScanSliceLastAllForVisiblePageAsync(
                id, Now, null, null, 1, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cappedRows);
        var cappedPage = await capped.Sut.SliceLastAllPageAsync(id, Now, limit: 1);
        cappedPage.Records.Should().BeEmpty();
        cappedPage.NextAfterDimensionSetId.Should().Be(cappedRows[^1].DimensionSetId);
        cappedPage.HasMore.Should().BeTrue();

        var maximumLimit = new Fixture();
        maximumLimit.Reader.Setup(x => x.ScanSliceLastAllForVisiblePageAsync(
                id, Now, null, null, int.MaxValue, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var maximumPage = await maximumLimit.Sut.SliceLastAllPageAsync(id, Now, limit: int.MaxValue);
        maximumPage.Records.Should().BeEmpty();
        maximumPage.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task FilteredSlice_DelegatesEmptyFiltersValidatesAndCoversDeletedAndVisiblePaging()
    {
        var id = Guid.NewGuid();
        var dimension = new DimensionValue(Guid.NewGuid(), Guid.NewGuid());
        var cursor = Guid.NewGuid();

        var delegated = new Fixture();
        delegated.Reader.Setup(x => x.ScanSliceLastAllForVisiblePageAsync(
                id, Now, null, null, 2, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        (await delegated.Sut.SliceLastAllFilteredAsync(id, Now, null, limit: 2)).Should().BeEmpty();
        (await delegated.Sut.SliceLastAllFilteredPageAsync(id, Now, [], limit: 2)).Records.Should().BeEmpty();

        var validation = new Fixture();
        await ((Func<Task>)(() => validation.Sut.SliceLastAllFilteredPageAsync(Guid.Empty, Now, [dimension])))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => validation.Sut.SliceLastAllFilteredPageAsync(id, LocalTime(), [dimension])))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        await ((Func<Task>)(() => validation.Sut.SliceLastAllFilteredPageAsync(id, Now, [dimension], limit: 0)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();

        var included = new Fixture();
        included.Reader.SetupSequence(x => x.SliceLastAllFilteredPageByDimensionsAsync(id, Now,
                It.Is<IReadOnlyList<DimensionValue>>(d => d.Count == 1), null, cursor, 2, true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .ReturnsAsync([Record(Guid.NewGuid()), Record(Guid.NewGuid(), true)]);
        var empty = await included.Sut.SliceLastAllFilteredPageAsync(id, Now, [dimension, dimension],
            afterDimensionSetId: cursor, limit: 2, includeDeleted: true);
        empty.NextAfterDimensionSetId.Should().Be(cursor);
        var full = await included.Sut.SliceLastAllFilteredPageAsync(id, Now, [dimension],
            afterDimensionSetId: cursor, limit: 2, includeDeleted: true);
        full.Records.Should().HaveCount(2);
        full.HasMore.Should().BeTrue();

        var visible = new Fixture();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        visible.Reader.Setup(x => x.ScanSliceLastAllFilteredForVisiblePageAsync(id, Now,
                It.IsAny<IReadOnlyList<DimensionValue>>(), null, null, 2, 25,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([Record(first), Record(second)]);
        var page = await visible.Sut.SliceLastAllFilteredPageAsync(id, Now, [dimension], limit: 2);
        page.Records.Select(x => x.DimensionSetId).Should().Equal(first, second);
        page.HasMore.Should().BeTrue();
    }

    [Fact]
    public async Task FilteredSlice_HandlesEmptyVisiblePageWithOnePersistenceQuery()
    {
        var id = Guid.NewGuid();
        var dimension = new DimensionValue(Guid.NewGuid(), Guid.NewGuid());
        var empty = new Fixture();
        empty.Reader.Setup(x => x.ScanSliceLastAllFilteredForVisiblePageAsync(id, Now,
                It.IsAny<IReadOnlyList<DimensionValue>>(), null, null, 1, 25,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        (await empty.Sut.SliceLastAllFilteredPageAsync(id, Now, [dimension], limit: 1)).Records.Should().BeEmpty();
        empty.Reader.Verify(x => x.ScanSliceLastAllFilteredForVisiblePageAsync(id, Now,
            It.IsAny<IReadOnlyList<DimensionValue>>(), null, null, 1, 25,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnrichedSlice_CoversEmptyMissingBagsEmptyKeysResolvedAndFallbackDisplays()
    {
        var id = Guid.NewGuid();

        var empty = new Fixture();
        empty.Reader.Setup(x => x.ScanSliceLastAllForVisiblePageAsync(
                id, Now, null, null, 3, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var emptyPage = await empty.Sut.SliceLastAllEnrichedPageAsync(id, Now, limit: 3);
        emptyPage.Records.Should().BeEmpty();
        (await empty.Sut.SliceLastAllEnrichedAsync(id, Now, limit: 3)).Should().BeEmpty();

        var missingBag = new Fixture();
        var missingSet = Guid.NewGuid();
        missingBag.Reader.Setup(x => x.ScanSliceLastAllForVisiblePageAsync(
                id, Now, null, null, 3, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Record(missingSet)]);
        missingBag.Bags.Setup(x => x.GetBagsByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DimensionBag>());
        var missingSnapshot = (await missingBag.Sut.SliceLastAllEnrichedPageAsync(id, Now, limit: 3)).Records[0];
        missingSnapshot.Dimensions.IsEmpty.Should().BeTrue();
        missingSnapshot.DimensionValueDisplaysByDimensionId.Should().BeEmpty();
        missingBag.Enrichment.Verify(x => x.ResolveAsync(It.IsAny<IReadOnlyCollection<DimensionValueKey>>(),
            It.IsAny<CancellationToken>()), Times.Never);

        var enriched = new Fixture();
        var setId = Guid.NewGuid();
        var resolvedDimension = Guid.NewGuid();
        var fallbackDimension = Guid.NewGuid();
        var resolvedValue = Guid.NewGuid();
        var fallbackValue = Guid.Parse("abcdef12-0000-0000-0000-000000000000");
        var bag = new DimensionBag([
            new DimensionValue(resolvedDimension, resolvedValue),
            new DimensionValue(fallbackDimension, fallbackValue)
        ]);
        enriched.Reader.Setup(x => x.ScanSliceLastAllForVisiblePageAsync(
                id, Now, null, null, 3, 25, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Record(setId), Record(setId)]);
        enriched.Bags.Setup(x => x.GetBagsByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DimensionBag> { [setId] = bag });
        enriched.Enrichment.Setup(x => x.ResolveAsync(It.IsAny<IReadOnlyCollection<DimensionValueKey>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<DimensionValueKey, string>
            {
                [new DimensionValueKey(resolvedDimension, resolvedValue)] = "Resolved"
            });
        var snapshots = await enriched.Sut.SliceLastAllEnrichedAsync(id, Now, limit: 3);
        snapshots.Should().HaveCount(2);
        snapshots[0].DimensionValueDisplaysByDimensionId[resolvedDimension].Should().Be("Resolved");
        snapshots[0].DimensionValueDisplaysByDimensionId[fallbackDimension].Should().Be("abcdef12");
    }

    [Fact]
    public async Task History_ValidatesTimesCursorLimitAndFiltersDeletedRows()
    {
        var f = new Fixture();
        var id = Guid.NewGuid();
        var setId = Guid.NewGuid();
        var before = Now.AddMinutes(-1);
        var dimension = new DimensionValue(Guid.NewGuid(), Guid.NewGuid());
        await ((Func<Task>)(() => f.Sut.GetKeyHistoryByDimensionSetIdAsync(Guid.Empty, setId, Now)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.GetKeyHistoryByDimensionSetIdAsync(id, setId, LocalTime())))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        await ((Func<Task>)(() => f.Sut.GetKeyHistoryByDimensionSetIdAsync(id, setId, Now, periodUtc: LocalTime())))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        await ((Func<Task>)(() => f.Sut.GetKeyHistoryByDimensionSetIdAsync(id, setId, Now,
            beforeRecordedAtUtc: LocalTime(), beforeRecordId: 1))).Should().ThrowAsync<NgbArgumentInvalidException>();
        await ((Func<Task>)(() => f.Sut.GetKeyHistoryByDimensionSetIdAsync(id, setId, Now,
            beforeRecordedAtUtc: before))).Should().ThrowAsync<NgbArgumentInvalidException>();
        await ((Func<Task>)(() => f.Sut.GetKeyHistoryByDimensionSetIdAsync(id, setId, Now,
            beforeRecordId: 1))).Should().ThrowAsync<NgbArgumentInvalidException>();
        await ((Func<Task>)(() => f.Sut.GetKeyHistoryByDimensionSetIdAsync(id, setId, Now, limit: 0)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();

        var active = Record(setId);
        var deleted = Record(setId, true);
        f.Reader.Setup(x => x.ListKeyHistoryAsync(id, It.IsAny<Guid>(), Now, null, null,
                before, 5, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync([active, deleted]);
        var included = await f.Sut.GetKeyHistoryAsync(id, [dimension, dimension], Now,
            beforeRecordedAtUtc: before, beforeRecordId: 5, limit: 2, includeDeleted: true);
        included.Should().HaveCount(2);

        f.Reader.Setup(x => x.ListKeyHistoryAsync(id, Guid.Empty, Now, null, null,
                null, null, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync([active, deleted]);
        var visible = await f.Sut.GetKeyHistoryAsync(id, null, Now, limit: 2);
        visible.Should().ContainSingle().Which.IsDeleted.Should().BeFalse();

        await f.Sut.GetKeyHistoryAsync(id, [], Now, limit: 2, includeDeleted: true);
    }

    private sealed class Fixture
    {
        public Mock<IReferenceRegisterRecordsReader> Reader { get; } = new(MockBehavior.Loose);
        public Mock<IDimensionSetReader> Bags { get; } = new(MockBehavior.Loose);
        public Mock<IDimensionValueEnrichmentReader> Enrichment { get; } = new(MockBehavior.Loose);
        public ReferenceRegisterReadService Sut { get; }
        public Fixture() => Sut = new(Reader.Object, Bags.Object, Enrichment.Object);
    }

    private static ReferenceRegisterRecordRead Record(Guid setId, bool deleted = false)
        => new(1, setId, null, null, null, Now, deleted, new Dictionary<string, object?>());

    private static DateTime LocalTime() => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Local);
}
