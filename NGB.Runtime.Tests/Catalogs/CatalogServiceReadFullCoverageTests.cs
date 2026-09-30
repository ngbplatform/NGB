using FluentAssertions;
using Moq;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Core.Catalogs.Exceptions;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Persistence.Catalogs.Universal;
using NGB.Persistence.Common;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Catalogs;

public sealed class CatalogServiceReadFullCoverageTests
{
    [Fact]
    public async Task Metadata_MapsEveryShapeAndSortsTypes()
    {
        var fixture = new CatalogServiceTestFixture();
        fixture.AddMetadata(CatalogServiceTestFixture.RichMetadata(
            "aaa",
            tables: [new CatalogTableMetadata("unused", TableKind.Part, [], [], "empty_part")]));
        fixture.AddMetadata(CatalogServiceTestFixture.RichMetadata(
            "simple",
            computedDisplay: false,
            tables:
            [
                new CatalogTableMetadata("cat_simple", TableKind.Head,
                    [new CatalogColumnMetadata("display", ColumnType.String)], [])
            ]));
        var sut = fixture.CreateService();

        var all = await sut.GetAllMetadataAsync(default);
        var rich = all.Single(x => x.CatalogType == "rich");

        all.Select(x => x.CatalogType).Should().Equal("aaa", "rich", "simple");
        all.Single(x => x.CatalogType == "aaa").List!.Columns.Should().BeEmpty();
        rich.Kind.Should().Be(EntityKind.Catalog);
        rich.Icon.Should().BeNull();
        rich.List!.Columns.Should().HaveCount(6);
        rich.List.Columns[0].Lookup.Should().BeOfType<CatalogLookupSourceDto>();
        rich.List.Columns[0].Options.Should().ContainSingle()
            .Which.Should().Be(new MetadataOptionDto("a", "Option A"));
        rich.List.Columns[5].Lookup.Should().BeOfType<DocumentLookupSourceDto>();

        var fields = rich.Form!.Sections.Single().Rows.SelectMany(x => x.Fields).ToDictionary(x => x.Key);
        fields["display"].Should().Match<FieldMetadataDto>(x =>
            x.Label == "Display label" && x.DataType == DataType.String && x.UiControl == UiControl.Input
            && x.IsRequired && x.IsReadOnly && x.Validation!.MaxLength == 40);
        fields["count32"].UiControl.Should().Be(UiControl.Number);
        fields["count64"].DataType.Should().Be(DataType.Int32);
        fields["amount"].DataType.Should().Be(DataType.Decimal);
        fields["enabled"].UiControl.Should().Be(UiControl.Checkbox);
        fields["document_id"].Lookup.Should().BeOfType<DocumentLookupSourceDto>();
        fields["day"].UiControl.Should().Be(UiControl.Date);
        fields["moment"].UiControl.Should().Be(UiControl.DateTime);
        fields["account_id"].Lookup.Should().BeOfType<ChartOfAccountsLookupSourceDto>();
        fields["plain"].Validation.Should().BeNull();
        rich.Parts.Should().ContainSingle().Which.Should().Match<PartMetadataDto>(x =>
            x.PartCode == "line_items" && x.Title == "Line Items" && x.List.Columns.Count == 2);
        rich.Capabilities.Should().Be(new CatalogCapabilitiesDto());
        all.Single(x => x.CatalogType == "simple").Should().Match<CatalogTypeMetadataDto>(x =>
            x.Parts == null && !x.Form!.Sections.Single().Rows.Single().Fields.Single().IsReadOnly);

        var single = await sut.GetTypeMetadataAsync("rich", default);
        single.Should().BeEquivalentTo(rich);
    }

    [Fact]
    public async Task Metadata_RejectsBlankTypeMissingHeadEmptyDisplayAndUnsupportedLookup()
    {
        var fixture = new CatalogServiceTestFixture();
        fixture.AddMetadata(CatalogServiceTestFixture.RichMetadata("no-head", tables: []));
        fixture.AddMetadata(CatalogServiceTestFixture.RichMetadata("no-display", displayColumn: " "));
        fixture.AddMetadata(CatalogServiceTestFixture.RichMetadata("bad-lookup", tables:
        [
            new CatalogTableMetadata("bad", TableKind.Head,
                [new CatalogColumnMetadata("display", ColumnType.String, Lookup: new UnsupportedLookup())], [])
        ]));
        var sut = fixture.CreateService();

        await ((Func<Task>)(() => sut.GetTypeMetadataAsync(" ", default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => sut.GetTypeMetadataAsync("no-head", default)))
            .Should().ThrowAsync<NgbConfigurationViolationException>();
        await ((Func<Task>)(() => sut.GetTypeMetadataAsync("no-display", default)))
            .Should().ThrowAsync<NgbConfigurationViolationException>();
        await ((Func<Task>)(() => sut.GetTypeMetadataAsync("bad-lookup", default)))
            .Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    [Fact]
    public async Task Page_CoversEmptyAndEnrichedRowsSearchScalarAndAllSoftDeleteModes()
    {
        var fixture = new CatalogServiceTestFixture();
        var seen = new List<CatalogQuery>();
        var pageCalls = 0;
        var id = Guid.NewGuid();
        fixture.Reader.Setup(x => x.CountAsync(It.IsAny<CatalogHeadDescriptor>(), It.IsAny<CatalogQuery>(),
                It.IsAny<CancellationToken>()))
            .Callback<CatalogHeadDescriptor, CatalogQuery, CancellationToken>((_, q, _) => seen.Add(q))
            .ReturnsAsync(7);
        fixture.Reader.Setup(x => x.GetPageAsync(It.IsAny<CatalogHeadDescriptor>(),
                It.IsAny<CatalogQuery>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++pageCalls == 2
                ? [CatalogServiceTestFixture.Row(id, new Dictionary<string, object?>
                {
                    ["display"] = "A",
                    ["count32"] = 2
                }, marked: true)]
                : []);
        var sut = fixture.CreateService();

        var empty = await sut.GetPageAsync("rich", new PageRequestDto(2, 3, "   "), default);
        empty.Should().BeEquivalentTo(new PageResponseDto<NGB.Contracts.Services.CatalogItemDto>([], 2, 3, 7));
        fixture.Enricher.Verify(x => x.EnrichCatalogItemsAsync(
            It.IsAny<CatalogHeadDescriptor>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyList<NGB.Contracts.Services.CatalogItemDto>>(), It.IsAny<CancellationToken>()), Times.Never);

        var page = await sut.GetPageAsync("rich", new PageRequestDto(0, 10, "find",
            new Dictionary<string, string>
            {
                ["filters.display"] = "A",
                ["plain"] = "B",
                [" "] = "ignored",
                ["trash"] = "active"
            }), default);
        page.Items.Should().ContainSingle().Which.Should().Match<NGB.Contracts.Services.CatalogItemDto>(x =>
            x.Id == id && x.IsMarkedForDeletion && !x.IsDeleted && x.Payload.Parts == null);
        seen[0].Should().Match<CatalogQuery>(x => x.Search == null
            && x.Filters.Count == 0 && x.SoftDeleteFilterMode == SoftDeleteFilterMode.All);
        seen[1].Should().Match<CatalogQuery>(x => x.Search == "find"
            && x.Filters.SequenceEqual(new[] { new CatalogFilter("display", "A"), new CatalogFilter("plain", "B") })
            && x.SoftDeleteFilterMode == SoftDeleteFilterMode.Active);
        fixture.Enricher.Verify(x => x.EnrichCatalogItemsAsync(
            It.IsAny<CatalogHeadDescriptor>(), "rich",
            It.Is<IReadOnlyList<NGB.Contracts.Services.CatalogItemDto>>(items => items.Count == 1),
            It.IsAny<CancellationToken>()), Times.Once);

        foreach (var value in new string?[] { null, "", "all", "false", "0", "deleted", "true", "1" })
        {
            var filters = new Dictionary<string, string> { ["deleted"] = value! };
            await sut.GetPageAsync("rich", new PageRequestDto(Filters: filters), default);
        }

        seen.Skip(2).Select(x => x.SoftDeleteFilterMode).Should().Equal(
            SoftDeleteFilterMode.All, SoftDeleteFilterMode.All, SoftDeleteFilterMode.All,
            SoftDeleteFilterMode.Active, SoftDeleteFilterMode.Active,
            SoftDeleteFilterMode.Deleted, SoftDeleteFilterMode.Deleted, SoftDeleteFilterMode.Deleted);
    }

    [Fact]
    public async Task Page_UsesCombinedReaderCapabilityInsteadOfSeparateCountAndPageCalls()
    {
        var fixture = new CatalogServiceTestFixture();
        var id = Guid.NewGuid();
        var combined = fixture.Reader.As<ICatalogCombinedPageReader>();
        combined.Setup(x => x.GetPageWithTotalAsync(
                It.IsAny<CatalogHeadDescriptor>(),
                It.IsAny<CatalogQuery>(),
                4,
                2,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogHeadQueryPage(
                [CatalogServiceTestFixture.Row(id, new Dictionary<string, object?> { ["display"] = "A" })],
                9));

        var page = await fixture.CreateService().GetPageAsync(
            "rich", new PageRequestDto(4, 2), default);

        page.Total.Should().Be(9);
        page.Items.Should().ContainSingle().Which.Id.Should().Be(id);
        fixture.Reader.Verify(x => x.CountAsync(
            It.IsAny<CatalogHeadDescriptor>(), It.IsAny<CatalogQuery>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Reader.Verify(x => x.GetPageAsync(
            It.IsAny<CatalogHeadDescriptor>(), It.IsAny<CatalogQuery>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Page_CanSkipExactTotalWithoutUsingCombinedCountQuery()
    {
        var fixture = new CatalogServiceTestFixture();
        var combined = fixture.Reader.As<ICatalogCombinedPageReader>();
        fixture.Reader.Setup(x => x.GetPageAsync(
                It.IsAny<CatalogHeadDescriptor>(),
                It.IsAny<CatalogQuery>(),
                4,
                2,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var page = await fixture.CreateService().GetPageAsync(
            "rich", new PageRequestDto(4, 2, IncludeTotal: false), default);

        page.Total.Should().BeNull();
        fixture.Reader.Verify(x => x.CountAsync(
            It.IsAny<CatalogHeadDescriptor>(), It.IsAny<CatalogQuery>(), It.IsAny<CancellationToken>()), Times.Never);
        combined.Verify(x => x.GetPageWithTotalAsync(
            It.IsAny<CatalogHeadDescriptor>(), It.IsAny<CatalogQuery>(),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Page_UsesSeekCapability_NormalizesBounds_AndHandlesEveryContinuationShape()
    {
        var fixture = new CatalogServiceTestFixture();
        var seek = fixture.Reader.As<ICatalogSeekPageReader>();
        var firstId = Guid.NewGuid();
        var calls = new List<(string? AfterDisplay, Guid? AfterId, int Limit, bool IncludeTotal)>();
        var responses = new Queue<CatalogHeadSeekPage>(
        [
            new CatalogHeadSeekPage(
                [CatalogServiceTestFixture.Row(firstId)],
                Total: 1,
                HasMore: true,
                NextAfterDisplay: "Zulu",
                NextAfterId: firstId),
            new CatalogHeadSeekPage([], null, HasMore: false, null, null),
            new CatalogHeadSeekPage([], null, HasMore: true, null, null),
            new CatalogHeadSeekPage([], null, HasMore: false, null, null)
        ]);
        seek.Setup(x => x.GetSeekPageAsync(
                It.IsAny<CatalogHeadDescriptor>(),
                It.IsAny<CatalogQuery>(),
                It.IsAny<string?>(),
                It.IsAny<Guid?>(),
                It.IsAny<int>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Callback<CatalogHeadDescriptor, CatalogQuery, string?, Guid?, int, bool, CancellationToken>(
                (_, _, afterDisplay, afterId, limit, includeTotal, _) =>
                    calls.Add((afterDisplay, afterId, limit, includeTotal)))
            .ReturnsAsync(() => responses.Dequeue());
        var sut = fixture.CreateService();

        var first = await sut.GetPageAsync(
            "rich",
            new PageRequestDto(Limit: PagingLimits.MaxPageSize + 1, Search: "   "),
            default);
        var second = await sut.GetPageAsync(
            "rich",
            new PageRequestDto(Limit: 2, Cursor: $"  {first.NextCursor}  "),
            default);
        var missingContinuationKey = await sut.GetPageAsync(
            "rich",
            new PageRequestDto(Limit: 2, Cursor: first.NextCursor),
            default);
        var withoutTotal = await sut.GetPageAsync(
            "rich",
            new PageRequestDto(Limit: 2, IncludeTotal: false),
            default);

        first.Limit.Should().Be(PagingLimits.MaxPageSize);
        first.NextCursor.Should().NotBeNullOrWhiteSpace();
        second.NextCursor.Should().BeNull();
        missingContinuationKey.HasMore.Should().BeTrue();
        missingContinuationKey.NextCursor.Should().BeNull();
        withoutTotal.Total.Should().BeNull();
        calls.Should().Equal(
            (null, null, PagingLimits.MaxPageSize, true),
            ("Zulu", firstId, 2, false),
            ("Zulu", firstId, 2, false),
            (null, null, 2, false));
    }

    [Fact]
    public async Task Page_RejectsUnknownScalarAndInvalidSoftDeleteFilters()
    {
        var sut = new CatalogServiceTestFixture().CreateService();

        await ((Func<Task>)(() => sut.GetPageAsync("rich",
                new PageRequestDto(Filters: new Dictionary<string, string> { ["missing_field"] = "x" }), default)))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        await ((Func<Task>)(() => sut.GetPageAsync("rich",
                new PageRequestDto(Filters: new Dictionary<string, string> { ["filters.deleted"] = "sometimes" }), default)))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
    }

    [Fact]
    public async Task Page_rejects_invalid_paging_combinations_before_reading()
    {
        var fixture = new CatalogServiceTestFixture();
        var sut = fixture.CreateService();

        await ((Func<Task>)(() => sut.GetPageAsync("rich", null!, default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => sut.GetPageAsync("rich", new PageRequestDto(Offset: -1), default)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => sut.GetPageAsync("rich", new PageRequestDto(Limit: 0), default)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => sut.GetPageAsync(
                "rich", new PageRequestDto(Offset: 1, Cursor: " cursor "), default)))
            .Should().ThrowAsync<NgbArgumentInvalidException>();

        fixture.Reader.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetById_CoversEmptyMissingNoPartsAndEveryPartRowShape()
    {
        var fixture = new CatalogServiceTestFixture();
        var simple = CatalogServiceTestFixture.RichMetadata("simple", tables:
        [
            new CatalogTableMetadata("cat_simple", TableKind.Head,
                [new CatalogColumnMetadata("display", ColumnType.String)], [])
        ]);
        fixture.AddMetadata(simple);
        var id = Guid.NewGuid();
        fixture.Reader.SetupSequence(x => x.GetByIdAsync(It.IsAny<CatalogHeadDescriptor>(), id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogHeadRow?)null)
            .ReturnsAsync(CatalogServiceTestFixture.Row(id, new Dictionary<string, object?> { ["display"] = "Simple" }))
            .ReturnsAsync(CatalogServiceTestFixture.Row(id, new Dictionary<string, object?>
            {
                ["display"] = "Rich",
                ["amount"] = 12.5m
            }));
        fixture.PartsReader.Setup(x => x.GetPartsAsync(It.IsAny<IReadOnlyList<CatalogTableMetadata>>(), id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>
            {
                ["cat_rich__lines"] =
                [
                    new Dictionary<string, object?>
                    {
                        ["catalog_id"] = id,
                        ["name"] = "Line",
                        ["extra"] = "ignored"
                    }
                ]
            });
        var sut = fixture.CreateService();

        await ((Func<Task>)(() => sut.GetByIdAsync("rich", Guid.Empty, default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => sut.GetByIdAsync("rich", id, default)))
            .Should().ThrowAsync<CatalogNotFoundException>();

        var noParts = await sut.GetByIdAsync("simple", id, default);
        noParts.Payload.Parts.Should().BeNull();

        var rich = await sut.GetByIdAsync("rich", id, default);
        rich.Payload.Parts.Should().ContainKey("line_items");
        var row = rich.Payload.Parts!["line_items"].Rows.Single();
        row.Keys.Should().BeEquivalentTo("name", "quantity");
        row["name"].GetString().Should().Be("Line");
        row["quantity"].ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
    }

    [Fact]
    public async Task GetById_MissingPartTableBecomesEmptyPart()
    {
        var fixture = new CatalogServiceTestFixture();
        var id = Guid.NewGuid();
        fixture.Reader.Setup(x => x.GetByIdAsync(It.IsAny<CatalogHeadDescriptor>(), id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CatalogServiceTestFixture.Row(id));
        fixture.PartsReader.Setup(x => x.GetPartsAsync(It.IsAny<IReadOnlyList<CatalogTableMetadata>>(), id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>());

        var item = await fixture.CreateService().GetByIdAsync("rich", id, default);

        item.Payload.Parts!["line_items"].Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Lookups_CoverGuardsDeduplicationEmptyAndMappedResults()
    {
        var fixture = new CatalogServiceTestFixture();
        fixture.AddMetadata(CatalogServiceTestFixture.RichMetadata("other"));
        var id = Guid.NewGuid();
        fixture.Reader.Setup(x => x.LookupAcrossTypesAsync(
                It.IsAny<IReadOnlyList<CatalogHeadDescriptor>>(), "q", 2, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CatalogLookupSearchRow(id, "rich", null, true)]);
        fixture.Reader.Setup(x => x.LookupAsync(It.IsAny<CatalogHeadDescriptor>(), "q", 3,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new CatalogLookupRow(id, "label")]);
        fixture.Reader.SetupSequence(x => x.GetByIdsAsync(It.IsAny<CatalogHeadDescriptor>(),
                It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .ReturnsAsync([new CatalogLookupRow(id, "by-id")]);
        var sut = fixture.CreateService();

        await ((Func<Task>)(() => sut.LookupAcrossTypesAsync(null!, null, 1, false, default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        (await sut.LookupAcrossTypesAsync(["rich"], null, 0, false, default)).Should().BeEmpty();
        (await sut.LookupAcrossTypesAsync([], null, 1, false, default)).Should().BeEmpty();
        (await sut.LookupAcrossTypesAsync([" ", ""], null, 1, false, default)).Should().BeEmpty();

        var across = await sut.LookupAcrossTypesAsync(["rich", "RICH", "other", " "], "q", 2, true, default);
        across.Should().ContainSingle().Which.Should().Match<NGB.Contracts.Services.CatalogLookupDto>(x =>
            x.Id == id && x.CatalogType == "rich" && x.Display == null && x.IsMarkedForDeletion);
        fixture.Reader.Verify(x => x.LookupAcrossTypesAsync(
            It.Is<IReadOnlyList<CatalogHeadDescriptor>>(heads => heads.Count == 2), "q", 2, true,
            It.IsAny<CancellationToken>()), Times.Once);

        (await sut.LookupAsync("rich", null, 0, default)).Should().BeEmpty();
        (await sut.LookupAsync("rich", "q", 3, default)).Should().ContainSingle()
            .Which.Should().Be(new NGB.Contracts.Services.LookupItemDto(id, "label"));
        await ((Func<Task>)(() => sut.GetByIdsAsync("rich", null!, default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        (await sut.GetByIdsAsync("rich", [], default)).Should().BeEmpty();
        (await sut.GetByIdsAsync("rich", [Guid.Empty, Guid.Empty], default)).Should().BeEmpty();
        (await sut.GetByIdsAsync("rich", [Guid.NewGuid()], default)).Should().BeEmpty();
        (await sut.GetByIdsAsync("rich", [id, Guid.Empty, id], default)).Should().Equal(
            new NGB.Contracts.Services.LookupItemDto(id, "by-id"),
            new NGB.Contracts.Services.LookupItemDto(id, "by-id"));

        var tooManyIds = Enumerable.Range(0, PagingLimits.MaxLookupIds + 1)
            .Select(_ => Guid.NewGuid())
            .ToArray();
        await ((Func<Task>)(() => sut.GetByIdsAsync("rich", tooManyIds, default)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Head_item_batch_lookup_validates_bounds_and_enriches_nonempty_results()
    {
        var fixture = new CatalogServiceTestFixture();
        var id = Guid.NewGuid();
        fixture.Reader.SetupSequence(x => x.GetByIdsWithFieldsAsync(
                It.IsAny<CatalogHeadDescriptor>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([])
            .ReturnsAsync([CatalogServiceTestFixture.Row(id)]);
        var sut = fixture.CreateService();

        await ((Func<Task>)(() => sut.GetHeadItemsByIdsAsync("rich", null!, default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        (await sut.GetHeadItemsByIdsAsync("rich", [], default)).Should().BeEmpty();
        (await sut.GetHeadItemsByIdsAsync("rich", [Guid.Empty, Guid.Empty], default)).Should().BeEmpty();
        (await sut.GetHeadItemsByIdsAsync("rich", [Guid.NewGuid()], default)).Should().BeEmpty();
        (await sut.GetHeadItemsByIdsAsync("rich", [id, id], default))
            .Should().ContainSingle().Which.Id.Should().Be(id);

        var tooManyIds = Enumerable.Range(0, PagingLimits.MaxLookupIds + 1)
            .Select(_ => Guid.NewGuid())
            .ToArray();
        await ((Func<Task>)(() => sut.GetHeadItemsByIdsAsync("rich", tooManyIds, default)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        fixture.Enricher.Verify(x => x.EnrichCatalogItemsAsync(
            It.IsAny<CatalogHeadDescriptor>(), "rich",
            It.Is<IReadOnlyList<NGB.Contracts.Services.CatalogItemDto>>(items => items.Count == 1),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Lookup_across_types_rejects_more_than_the_supported_distinct_type_count()
    {
        var fixture = new CatalogServiceTestFixture();
        var codes = Enumerable.Range(0, PagingLimits.MaxLookupTypes + 1)
            .Select(index => $"type-{index}")
            .ToArray();
        foreach (var code in codes)
            fixture.AddMetadata(CatalogServiceTestFixture.RichMetadata(code));

        var act = () => fixture.CreateService().LookupAcrossTypesAsync(codes, null, 1, false, default);

        await act.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        fixture.Reader.Verify(x => x.LookupAcrossTypesAsync(
            It.IsAny<IReadOnlyList<CatalogHeadDescriptor>>(), It.IsAny<string?>(),
            It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed record UnsupportedLookup : LookupSourceMetadata;
}
