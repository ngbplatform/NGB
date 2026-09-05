using System.Text.Json;
using FluentAssertions;
using NGB.Contracts.Metadata;
using NGB.Contracts.Common;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting.Exceptions;
using NGB.Runtime.Reporting.Canonical;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class CanonicalReportExecutionHelperFullCoverageTests
{
    [Fact]
    public void DateParameters_CoverMissingInvalidValidAndBoundaryRanges()
    {
        var definition = Definition();

        CanonicalReportExecutionHelper.GetOptionalDateOnlyParameter(
            definition, new ReportExecutionRequestDto(), "optional_date").Should().BeNull();
        CanonicalReportExecutionHelper.GetOptionalDateOnlyParameter(
            definition, Request(parameters: new Dictionary<string, string>()), "optional_date").Should().BeNull();
        CanonicalReportExecutionHelper.GetOptionalDateOnlyParameter(
            definition, Request(parameters: new Dictionary<string, string> { ["optional_date"] = "  " }), "optional_date").Should().BeNull();

        var missingRequired = () => CanonicalReportExecutionHelper.GetRequiredDateOnlyParameter(
            definition, Request(parameters: new Dictionary<string, string>()), "from_utc");
        missingRequired.Should().Throw<ReportLayoutValidationException>().WithMessage("*From*required*");

        var blankRequired = () => CanonicalReportExecutionHelper.GetRequiredDateOnlyParameter(
            definition, Request(parameters: new Dictionary<string, string> { ["from_utc"] = " \t " }), "from_utc");
        blankRequired.Should().Throw<ReportLayoutValidationException>().WithMessage("*From*required*");

        var invalidRequired = () => CanonicalReportExecutionHelper.GetRequiredDateOnlyParameter(
            definition, Request(parameters: new Dictionary<string, string> { ["from_utc"] = "2026-02-30" }), "from_utc");
        invalidRequired.Should().Throw<ReportLayoutValidationException>().WithMessage("*valid date*From*");

        var invalidOptional = () => CanonicalReportExecutionHelper.GetOptionalDateOnlyParameter(
            definition, Request(parameters: new Dictionary<string, string> { ["optional_date"] = "01/02/2026" }), "optional_date");
        invalidOptional.Should().Throw<ReportLayoutValidationException>().WithMessage("*yyyy-MM-dd*");

        CanonicalReportExecutionHelper.GetOptionalDateOnlyParameter(
                definition,
                Request(parameters: new Dictionary<string, string> { ["optional_date"] = " 2026-08-21 " }),
                "optional_date")
            .Should().Be(new DateOnly(2026, 8, 21));

        var reversed = () => CanonicalReportExecutionHelper.GetRequiredDateRange(
            definition,
            Request(parameters: new Dictionary<string, string>
            {
                ["from_utc"] = "2026-02-01",
                ["to_utc"] = "2026-01-31"
            }));
        reversed.Should().Throw<ReportLayoutValidationException>().WithMessage("To*on or after*From*");

        var sameDay = CanonicalReportExecutionHelper.GetRequiredDateRange(
            definition,
            Request(parameters: new Dictionary<string, string>
            {
                ["from_utc"] = "2026-08-31",
                ["to_utc"] = "2026-08-31"
            }));
        sameDay.Should().Be((
            new DateOnly(2026, 8, 31),
            new DateOnly(2026, 8, 31),
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 8, 1)));
    }

    [Fact]
    public void OptionalSingleGuidFilter_CoversAllSupportedShapesAndRequiredBehavior()
    {
        var definition = Definition();
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");

        CanonicalReportExecutionHelper.GetOptionalGuidFilter(definition, new ReportExecutionRequestDto(), "single_id")
            .Should().BeNull();
        CanonicalReportExecutionHelper.GetOptionalGuidFilter(definition, FilterRequest("single_id", default), "single_id")
            .Should().BeNull();
        CanonicalReportExecutionHelper.GetOptionalGuidFilter(definition, FilterRequest("single_id", JsonValue<object?>(null)), "single_id")
            .Should().BeNull();
        CanonicalReportExecutionHelper.GetOptionalGuidFilter(definition, FilterRequest("single_id", JsonValue(Array.Empty<Guid>())), "single_id")
            .Should().BeNull();
        CanonicalReportExecutionHelper.GetOptionalGuidFilter(definition, FilterRequest("single_id", JsonValue(id)), "single_id")
            .Should().Be(id);
        CanonicalReportExecutionHelper.GetOptionalGuidFilter(definition, FilterRequest("single_id", JsonValue(new[] { id })), "single_id")
            .Should().Be(id);
        CanonicalReportExecutionHelper.GetRequiredGuidFilter(definition, FilterRequest("single_id", JsonValue(id)), "single_id")
            .Should().Be(id);

        var missing = () => CanonicalReportExecutionHelper.GetRequiredGuidFilter(
            definition, FilterRequest("single_id", JsonValue(Array.Empty<Guid>())), "single_id");
        missing.Should().Throw<ReportLayoutValidationException>().WithMessage("Single is required.");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("\"\"")]
    [InlineData("\"not-a-guid\"")]
    [InlineData("[42]")]
    [InlineData("[\"\"]")]
    [InlineData("[\"00000000-0000-0000-0000-000000000000\"]")]
    public void GuidFilters_RejectMalformedScalarAndArrayItems(string json)
    {
        var definition = Definition();
        var request = FilterRequest("multi_id", JsonDocument.Parse(json).RootElement.Clone());

        var act = () => CanonicalReportExecutionHelper.GetOptionalGuidFilters(definition, request, "multi_id");

        act.Should().Throw<ReportLayoutValidationException>().WithMessage("Select a valid Multiple.");
    }

    [Fact]
    public void GuidFilters_CoverPluralNullStringArrayDeduplicationAndSingleMultiplicityError()
    {
        var definition = Definition();
        var first = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var second = Guid.Parse("22222222-2222-2222-2222-222222222222");

        CanonicalReportExecutionHelper.GetOptionalGuidFilters(definition, new ReportExecutionRequestDto(), "multi_id")
            .Should().BeEmpty();
        CanonicalReportExecutionHelper.GetOptionalGuidFilters(definition, FilterRequest("multi_id", default), "multi_id")
            .Should().BeEmpty();
        CanonicalReportExecutionHelper.GetOptionalGuidFilters(definition, FilterRequest("multi_id", JsonValue<object?>(null)), "multi_id")
            .Should().BeEmpty();
        CanonicalReportExecutionHelper.GetOptionalGuidFilters(definition, FilterRequest("multi_id", JsonValue(first)), "multi_id")
            .Should().Equal(first);
        CanonicalReportExecutionHelper.GetOptionalGuidFilters(
                definition,
                FilterRequest("multi_id", JsonValue(new[] { first, second, first })),
                "multi_id")
            .Should().Equal(first, second);

        var tooMany = () => CanonicalReportExecutionHelper.GetOptionalGuidFilter(
            definition,
            FilterRequest("single_id", JsonValue(new[] { first, second })),
            "single_id");
        tooMany.Should().Throw<ReportLayoutValidationException>().WithMessage("Select a single Single.");

        var invalidKind = () => CanonicalReportExecutionHelper.GetOptionalGuidFilter(
            definition, FilterRequest("single_id", JsonValue(42)), "single_id");
        invalidKind.Should().Throw<ReportLayoutValidationException>().WithMessage("Select a valid Single.");

        var excessiveIds = Enumerable.Range(0, ReportLayoutLimits.MaxValuesPerFilter + 1)
            .Select(_ => Guid.NewGuid())
            .ToArray();
        var excessive = () => CanonicalReportExecutionHelper.GetOptionalGuidFilters(
            definition, FilterRequest("multi_id", JsonValue(excessiveIds)), "multi_id");
        excessive.Should().Throw<ReportLayoutValidationException>()
            .WithMessage($"Select up to {ReportLayoutLimits.MaxValuesPerFilter} Multiple values.");

        CanonicalReportExecutionHelper.GetOptionalGuidFilter(
                definition, FilterRequest(" single_id ", JsonValue(first)), "single_id")
            .Should().Be(first);
        CanonicalReportExecutionHelper.GetOptionalGuidFilter(
                definition, FilterRequest("other", JsonValue(first)), "single_id")
            .Should().BeNull();
    }

    [Fact]
    public void OptionalBoolFilter_CoversNativeBlankAndInvalidNonStringValues()
    {
        var definition = Definition();

        CanonicalReportExecutionHelper.GetOptionalBoolFilter(definition, new ReportExecutionRequestDto(), "flag")
            .Should().BeNull();
        CanonicalReportExecutionHelper.GetOptionalBoolFilter(definition, FilterRequest("flag", JsonValue(true)), "flag")
            .Should().BeTrue();
        CanonicalReportExecutionHelper.GetOptionalBoolFilter(definition, FilterRequest("flag", JsonValue(false)), "flag")
            .Should().BeFalse();
        CanonicalReportExecutionHelper.GetOptionalBoolFilter(definition, FilterRequest("flag", JsonValue(" \t ")), "flag")
            .Should().BeNull();

        var invalid = () => CanonicalReportExecutionHelper.GetOptionalBoolFilter(
            definition, FilterRequest("flag", JsonValue(1)), "flag");
        invalid.Should().Throw<ReportLayoutValidationException>().WithMessage("Select Yes or No for Flag.");
    }

    [Fact]
    public void BuildDimensionScopes_CoversNoMetadataSkippedFiltersEmptyIdsAndCatalogScope()
    {
        CanonicalReportExecutionHelper.BuildDimensionScopes(
                new ReportDefinitionDto("empty", "Empty"),
                new ReportExecutionRequestDto())
            .Should().BeNull();

        var definition = new ReportDefinitionDto(
            "scope.report",
            "Scope",
            Filters:
            [
                new ReportFilterFieldDto("unsupported", "Unsupported", "uuid"),
                new ReportFilterFieldDto("many_documents", "Many documents", "uuid", Lookup: new DocumentLookupSourceDto(["a", "b"])),
                new ReportFilterFieldDto("missing", "Missing", "uuid", Lookup: new CatalogLookupSourceDto("demo.missing")),
                new ReportFilterFieldDto("empty", "Empty", "uuid", IsMulti: true, Lookup: new CatalogLookupSourceDto("demo.empty")),
                new ReportFilterFieldDto("warehouse", "Warehouse", "uuid", IsMulti: true, Lookup: new CatalogLookupSourceDto("demo.warehouse"))
            ]);
        var warehouse = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var request = new ReportExecutionRequestDto(Filters: new Dictionary<string, ReportFilterValueDto>
        {
            ["empty"] = new(JsonValue(Array.Empty<Guid>())),
            ["warehouse"] = new(JsonValue(new[] { warehouse }), IncludeDescendants: true)
        });

        var scopes = CanonicalReportExecutionHelper.BuildDimensionScopes(definition, request);

        scopes.Should().ContainSingle();
        scopes!.Single().ValueIds.Should().Equal(warehouse);
        scopes.Single().IncludeDescendants.Should().BeTrue();

        var invalidDefinition = new ReportDefinitionDto(
            "scope.invalid",
            "Invalid scope",
            Filters:
            [
                new ReportFilterFieldDto("warehouse", "Warehouse", "uuid", Lookup: new CatalogLookupSourceDto("demo.warehouse"))
            ]);
        var invalid = () => CanonicalReportExecutionHelper.BuildDimensionScopes(
            invalidDefinition,
            FilterRequest("warehouse", JsonValue(42)));
        invalid.Should().Throw<ReportLayoutValidationException>().WithMessage("Select a valid Warehouse.");
    }

    [Fact]
    public void Labels_HumanizeMetadataCodesAndCoverAllFallbacks()
    {
        var definition = Definition();

        CanonicalReportExecutionHelper.GetParameterLabel(definition, "from_utc").Should().Be("From");
        CanonicalReportExecutionHelper.GetParameterLabel(definition, "label_missing").Should().Be("Label Missing");
        CanonicalReportExecutionHelper.GetParameterLabel(definition, "unknown_id").Should().Be("Unknown");
        CanonicalReportExecutionHelper.GetParameterLabel(new ReportDefinitionDto("r", "R"), "a_b").Should().Be("A B");
        CanonicalReportExecutionHelper.GetParameterLabel(new ReportDefinitionDto("r", "R"), "x").Should().Be("X");
        CanonicalReportExecutionHelper.GetParameterLabel(new ReportDefinitionDto("r", "R"), "utc").Should().Be("utc");
        CanonicalReportExecutionHelper.GetParameterLabel(new ReportDefinitionDto("r", "R"), "___").Should().Be("___");
        CanonicalReportExecutionHelper.GetParameterLabel(new ReportDefinitionDto("r", "R"), " ").Should().Be(" ");

        CanonicalReportExecutionHelper.GetFilterLabel(definition, "single_id").Should().Be("Single");
        CanonicalReportExecutionHelper.GetFilterLabel(definition, "label_missing").Should().Be("Label Missing");
        CanonicalReportExecutionHelper.GetFilterLabel(definition, "unknown_code").Should().Be("Unknown Code");
        CanonicalReportExecutionHelper.GetFilterLabel(new ReportDefinitionDto("r", "R"), "custom_code").Should().Be("Custom Code");
    }

    [Fact]
    public void PageJsonAndVariantHelpers_PreserveAllInputsAndNormalizeVariantCode()
    {
        var sheet = new ReportSheetDto([], []);
        var diagnostics = new Dictionary<string, string> { ["source"] = "test" };

        var page = CanonicalReportExecutionHelper.CreatePrebuiltPage(
            sheet, offset: int.MaxValue, limit: 0, total: null, hasMore: true, nextCursor: "next", diagnostics);

        page.PrebuiltSheet.Should().BeSameAs(sheet);
        page.Offset.Should().Be(int.MaxValue);
        page.Limit.Should().Be(0);
        page.Total.Should().BeNull();
        page.HasMore.Should().BeTrue();
        page.NextCursor.Should().Be("next");
        page.Diagnostics.Should().BeSameAs(diagnostics);
        CanonicalReportExecutionHelper.JsonValue(new { Value = 42 }).GetProperty("Value").GetInt32().Should().Be(42);
        CanonicalReportExecutionHelper.GetExecutorVariantCode(new ReportExecutionRequestDto()).Should().BeNull();
        CanonicalReportExecutionHelper.GetExecutorVariantCode(new ReportExecutionRequestDto(VariantCode: " \t ")).Should().BeNull();
        CanonicalReportExecutionHelper.GetExecutorVariantCode(new ReportExecutionRequestDto(VariantCode: "  Month-End  "))
            .Should().Be("Month-End");
    }

    [Fact]
    public void CreateBoundedPrebuiltPage_ReturnsFullSheetAndAppliesHardCap()
    {
        var definition = Definition();
        var rows = Enumerable.Range(0, 3)
            .Select(_ => new ReportSheetRowDto(ReportRowKind.Detail, []))
            .ToArray();
        var sheet = new ReportSheetDto([], rows);

        var page = CanonicalReportExecutionHelper.CreateBoundedPrebuiltPage(
            definition,
            sheet);
        page.PrebuiltSheet!.Rows.Should().HaveCount(3);
        page.Offset.Should().Be(0);
        page.Limit.Should().Be(3);
        page.Total.Should().Be(3);
        page.HasMore.Should().BeFalse();

        var oversized = new ReportSheetDto(
            [],
            Enumerable.Range(0, PagingLimits.MaxMaterializedRows + 1)
                .Select(_ => new ReportSheetRowDto(ReportRowKind.Detail, []))
                .ToArray());
        var action = () => CanonicalReportExecutionHelper.CreateBoundedPrebuiltPage(
            definition,
            oversized);
        action.Should().Throw<ReportLayoutValidationException>();
    }

    private static ReportDefinitionDto Definition()
        => new(
            "test.canonical.helper",
            "Helper",
            Parameters:
            [
                new ReportParameterMetadataDto("from_utc", "date", true, Label: "From"),
                new ReportParameterMetadataDto("to_utc", "date", true, Label: "To"),
                new ReportParameterMetadataDto("label_missing", "string", false, Label: null)
            ],
            Filters:
            [
                new ReportFilterFieldDto("single_id", "Single", "uuid"),
                new ReportFilterFieldDto("multi_id", "Multiple", "uuid", IsMulti: true),
                new ReportFilterFieldDto("flag", "Flag", "bool"),
                new ReportFilterFieldDto("label_missing", null!, "string")
            ]);

    private static ReportExecutionRequestDto Request(IReadOnlyDictionary<string, string>? parameters = null)
        => new(Parameters: parameters);

    private static ReportExecutionRequestDto FilterRequest(string code, JsonElement value)
        => new(Filters: new Dictionary<string, ReportFilterValueDto> { [code] = new(value) });

    private static JsonElement JsonValue<T>(T value) => JsonSerializer.SerializeToElement(value);
}
