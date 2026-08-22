using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Core.Dimensions;
using NGB.Runtime.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportFilterScopeExpanderFullCoverageTests
{
    [Fact]
    public async Task Expand_RejectsNullRuntimeAndRequest()
    {
        var sut = new ReportFilterScopeExpander();

        var nullRuntime = () => sut.ExpandAsync(null!, Request(), default);
        var nullRequest = () => sut.ExpandAsync(Runtime(), null!, default);

        (await nullRuntime.Should().ThrowAsync<NgbArgumentRequiredException>()).Which.ParamName.Should().Be("runtime");
        (await nullRequest.Should().ThrowAsync<NgbArgumentRequiredException>()).Which.ParamName.Should().Be("request");
    }

    [Fact]
    public async Task Expand_WhenEitherDependencyIsMissing_ReturnsOriginalRequest()
    {
        var request = Request(("field", GuidValue(Guid.CreateVersion7()), true));
        var runtime = Runtime();

        var withoutExpansion = await new ReportFilterScopeExpander(
            null,
            Mock.Of<IDimensionDefinitionReader>()).ExpandAsync(runtime, request, default);
        var withoutDefinitions = await new ReportFilterScopeExpander(
            Mock.Of<IDimensionScopeExpansionService>(),
            null).ExpandAsync(runtime, request, default);

        withoutExpansion.Should().BeSameAs(request);
        withoutDefinitions.Should().BeSameAs(request);
    }

    [Fact]
    public async Task Expand_WhenFiltersAreNullOrEmpty_ReturnsOriginalRequestWithoutReadingDefinitions()
    {
        var sut = CreateStrictSut();
        var nullFilters = Request();
        var emptyFilters = new ReportExecutionRequestDto(
            Filters: new Dictionary<string, ReportFilterValueDto>());

        var first = await sut.ExpandAsync(Runtime(), nullFilters, default);
        var second = await sut.ExpandAsync(Runtime(), emptyFilters, default);

        first.Should().BeSameAs(nullFilters);
        second.Should().BeSameAs(emptyFilters);
    }

    [Fact]
    public async Task Expand_WhenNoFilterCanBecomeAScope_ReturnsOriginalRequest()
    {
        var catalog = new CatalogLookupSourceDto("test.catalog");
        var runtime = Runtime(
            fields:
            [
                Field("attribute_lookup", ReportFieldKind.Attribute, catalog),
                Field("dimension_without_lookup", ReportFieldKind.Dimension),
                Field("dimension_coa", ReportFieldKind.Dimension, new ChartOfAccountsLookupSourceDto()),
                Field("dimension_multi_document", ReportFieldKind.Dimension, new DocumentLookupSourceDto(["doc.a", "doc.b"])),
                Field("invalid_string", ReportFieldKind.Dimension, catalog),
                Field("undefined", ReportFieldKind.Dimension, catalog),
                Field("empty_array", ReportFieldKind.Dimension, catalog),
                Field("empty_guid", ReportFieldKind.Dimension, catalog),
                Field("empty_guid_array", ReportFieldKind.Dimension, catalog),
                Field("invalid_array_item", ReportFieldKind.Dimension, catalog)
            ],
            filters:
            [
                Filter("filter_without_lookup"),
                Filter("filter_coa", new ChartOfAccountsLookupSourceDto()),
                Filter("filter_multi_document", new DocumentLookupSourceDto(["doc.a", "doc.b"]))
            ]);
        var request = Request(
            ("missing", GuidValue(Guid.CreateVersion7()), true),
            ("attribute_lookup", GuidValue(Guid.CreateVersion7()), true),
            ("dimension_without_lookup", GuidValue(Guid.CreateVersion7()), true),
            ("dimension_coa", GuidValue(Guid.CreateVersion7()), true),
            ("dimension_multi_document", GuidValue(Guid.CreateVersion7()), true),
            ("filter_without_lookup", GuidValue(Guid.CreateVersion7()), true),
            ("filter_coa", GuidValue(Guid.CreateVersion7()), true),
            ("filter_multi_document", GuidValue(Guid.CreateVersion7()), true),
            ("invalid_string", JsonSerializer.SerializeToElement("not-a-guid"), true),
            ("undefined", default, true),
            ("empty_array", JsonSerializer.SerializeToElement(Array.Empty<Guid>()), true),
            ("empty_guid", GuidValue(Guid.Empty), true),
            ("empty_guid_array", JsonSerializer.SerializeToElement(new[] { Guid.Empty }), true),
            ("invalid_array_item", JsonSerializer.SerializeToElement(new object[] { Guid.CreateVersion7(), 42 }), true));

        var result = await CreateStrictSut().ExpandAsync(runtime, request, default);

        var requestWithoutDefinitionFilters = Request(("also_missing", GuidValue(Guid.CreateVersion7()), true));
        var resultWithoutDefinitionFilters = await CreateStrictSut().ExpandAsync(
            Runtime(fields: []),
            requestWithoutDefinitionFilters,
            default);

        result.Should().BeSameAs(request);
        resultWithoutDefinitionFilters.Should().BeSameAs(requestWithoutDefinitionFilters);
    }

    [Fact]
    public async Task Expand_WhenCandidatesDoNotRequestDescendants_ReturnsAfterDefinitionLookup()
    {
        var dimensionId = Guid.CreateVersion7();
        var definitions = new Mock<IDimensionDefinitionReader>(MockBehavior.Strict);
        definitions.Setup(x => x.GetDimensionIdsByCodesAsync(
                It.Is<IReadOnlyCollection<string>>(codes => codes.SequenceEqual(new[] { "test.catalog" })),
                default))
            .ReturnsAsync(new Dictionary<string, Guid> { ["test.catalog"] = dimensionId });
        var request = Request(("catalog_id", GuidValue(Guid.CreateVersion7()), false));
        var sut = new ReportFilterScopeExpander(
            new Mock<IDimensionScopeExpansionService>(MockBehavior.Strict).Object,
            definitions.Object);

        var result = await sut.ExpandAsync(
            Runtime(fields: [Field("catalog_id", ReportFieldKind.Dimension, new CatalogLookupSourceDto("test.catalog"))]),
            request,
            default);

        result.Should().BeSameAs(request);
        definitions.VerifyAll();
    }

    [Fact]
    public async Task Expand_WhenRequestedDimensionIsUnknown_ReturnsWithoutCallingExpansionService()
    {
        var definitions = new Mock<IDimensionDefinitionReader>(MockBehavior.Strict);
        definitions.Setup(x => x.GetDimensionIdsByCodesAsync(It.IsAny<IReadOnlyCollection<string>>(), default))
            .ReturnsAsync(new Dictionary<string, Guid>());
        var request = Request(("document_id", GuidValue(Guid.CreateVersion7()), true));
        var sut = new ReportFilterScopeExpander(
            new Mock<IDimensionScopeExpansionService>(MockBehavior.Strict).Object,
            definitions.Object);

        var result = await sut.ExpandAsync(
            Runtime(filters: [Filter("document_id", new DocumentLookupSourceDto(["sales.invoice"]))]),
            request,
            default);

        result.Should().BeSameAs(request);
        definitions.VerifyAll();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Expand_WhenExpansionReturnsNullOrEmpty_ReturnsOriginalRequest(bool returnNull)
    {
        var dimensionId = Guid.CreateVersion7();
        var definitions = Definitions(("test.catalog", dimensionId));
        var expansion = new Mock<IDimensionScopeExpansionService>(MockBehavior.Strict);
        expansion.Setup(x => x.ExpandAsync(
                "test.report",
                It.Is<DimensionScopeBag>(bag => bag.Count == 1 && bag[0].DimensionId == dimensionId),
                default))
            .ReturnsAsync(returnNull ? null : DimensionScopeBag.Empty);
        var request = Request(("catalog_id", GuidValue(Guid.CreateVersion7()), true));
        var sut = new ReportFilterScopeExpander(expansion.Object, definitions.Object);

        var result = await sut.ExpandAsync(
            Runtime(fields: [Field("catalog_id", ReportFieldKind.Dimension, new CatalogLookupSourceDto("test.catalog"))]),
            request,
            default);

        result.Should().BeSameAs(request);
        definitions.VerifyAll();
        expansion.VerifyAll();
    }

    [Fact]
    public async Task Expand_ResolvesDatasetAndDefinitionLookups_DeduplicatesValuesAndIgnoresUnknownExpandedScope()
    {
        var sharedDimensionId = Guid.CreateVersion7();
        var documentDimensionId = Guid.CreateVersion7();
        var fallbackCatalogDimensionId = Guid.CreateVersion7();
        var fallbackDocumentDimensionId = Guid.CreateVersion7();
        var unknownExpandedDimensionId = Guid.CreateVersion7();
        var selected = Guid.CreateVersion7();
        var child = Guid.CreateVersion7();
        var document = Guid.CreateVersion7();
        var runtime = Runtime(
            fields:
            [
                Field("dataset_catalog", ReportFieldKind.Dimension, new CatalogLookupSourceDto("test.shared")),
                Field("same_dimension_without_expansion", ReportFieldKind.Dimension, new CatalogLookupSourceDto("test.same")),
                Field("dataset_document", ReportFieldKind.Dimension, new DocumentLookupSourceDto(["sales.invoice"])),
                Field("fallback_catalog", ReportFieldKind.Attribute, new ChartOfAccountsLookupSourceDto()),
                Field("fallback_document", ReportFieldKind.Dimension, new ChartOfAccountsLookupSourceDto())
            ],
            filters:
            [
                Filter("fallback_catalog", new CatalogLookupSourceDto("test.fallback_catalog")),
                Filter("fallback_document", new DocumentLookupSourceDto(["sales.order"]))
            ]);
        var request = Request(
            ("dataset_catalog", JsonSerializer.SerializeToElement(new[] { selected, Guid.Empty, selected }), true),
            ("same_dimension_without_expansion", GuidValue(selected), false),
            ("dataset_document", GuidValue(document), true),
            ("fallback_catalog", GuidValue(selected), true),
            ("fallback_document", GuidValue(document), true));
        var definitions = Definitions(
            ("test.shared", sharedDimensionId),
            ("test.same", sharedDimensionId),
            ("sales.invoice", documentDimensionId),
            ("test.fallback_catalog", fallbackCatalogDimensionId),
            ("sales.order", fallbackDocumentDimensionId));
        var expansion = new Mock<IDimensionScopeExpansionService>(MockBehavior.Strict);
        expansion.Setup(x => x.ExpandAsync(
                "test.report",
                It.Is<DimensionScopeBag>(bag =>
                    bag.Count == 4
                    && bag.All(scope => scope.IncludeDescendants)
                    && bag.Single(scope => scope.DimensionId == sharedDimensionId).ValueIds.SequenceEqual(new[] { selected })),
                default))
            .ReturnsAsync(new DimensionScopeBag(
            [
                new DimensionScope(sharedDimensionId, [selected, child]),
                new DimensionScope(documentDimensionId, [document]),
                new DimensionScope(fallbackCatalogDimensionId, [selected]),
                new DimensionScope(fallbackDocumentDimensionId, [document]),
                new DimensionScope(unknownExpandedDimensionId, [Guid.CreateVersion7()])
            ]));
        var sut = new ReportFilterScopeExpander(expansion.Object, definitions.Object);

        var result = await sut.ExpandAsync(runtime, request, default);

        result.Should().NotBeSameAs(request);
        result.Filters!["dataset_catalog"].IncludeDescendants.Should().BeFalse();
        result.Filters["dataset_catalog"].Value.EnumerateArray().Select(x => x.GetGuid()).Should().BeEquivalentTo([selected, child]);
        result.Filters["same_dimension_without_expansion"].IncludeDescendants.Should().BeFalse();
        result.Filters["dataset_document"].Value.EnumerateArray().Select(x => x.GetGuid()).Should().Equal(document);
        result.Filters["fallback_catalog"].Value.EnumerateArray().Select(x => x.GetGuid()).Should().Equal(selected);
        result.Filters["fallback_document"].Value.EnumerateArray().Select(x => x.GetGuid()).Should().Equal(document);
        definitions.VerifyAll();
        expansion.VerifyAll();
    }

    private static ReportFilterScopeExpander CreateStrictSut()
        => new(
            new Mock<IDimensionScopeExpansionService>(MockBehavior.Strict).Object,
            new Mock<IDimensionDefinitionReader>(MockBehavior.Strict).Object);

    private static Mock<IDimensionDefinitionReader> Definitions(params (string Code, Guid Id)[] values)
    {
        var result = values.ToDictionary(x => x.Code, x => x.Id, StringComparer.OrdinalIgnoreCase);
        var mock = new Mock<IDimensionDefinitionReader>(MockBehavior.Strict);
        mock.Setup(x => x.GetDimensionIdsByCodesAsync(It.IsAny<IReadOnlyCollection<string>>(), default))
            .ReturnsAsync(result);
        return mock;
    }

    private static ReportDefinitionRuntimeModel Runtime(
        IReadOnlyList<ReportFieldDto>? fields = null,
        IReadOnlyList<ReportFilterFieldDto>? filters = null)
        => new(new ReportDefinitionDto(
            "test.report",
            "Test report",
            Dataset: fields is null ? null : new ReportDatasetDto("test.dataset", fields),
            Filters: filters));

    private static ReportExecutionRequestDto Request(
        params (string Code, JsonElement Value, bool IncludeDescendants)[] filters)
        => new(Filters: filters.Length == 0
            ? null
            : filters.ToDictionary(
                x => x.Code,
                x => new ReportFilterValueDto(x.Value, x.IncludeDescendants),
                StringComparer.OrdinalIgnoreCase));

    private static ReportFieldDto Field(string code, ReportFieldKind kind, LookupSourceDto? lookup = null)
        => new(code, code, "uuid", kind, Lookup: lookup);

    private static ReportFilterFieldDto Filter(string code, LookupSourceDto? lookup = null)
        => new(code, code, "uuid", Lookup: lookup);

    private static JsonElement GuidValue(Guid value) => JsonSerializer.SerializeToElement(value);
}
