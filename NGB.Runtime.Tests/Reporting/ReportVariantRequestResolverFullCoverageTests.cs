using System.Text.Json;
using FluentAssertions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting.Exceptions;
using NGB.Runtime.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportVariantRequestResolverFullCoverageTests
{
    [Fact]
    public async Task ResolveAsync_WhenRequestIsNull_ThrowsRequiredArgument()
    {
        var resolver = new ReportVariantRequestResolver(new StubVariantService(null));

        var act = () => resolver.ResolveAsync("report", null!, CancellationToken.None);

        await act.Should().ThrowAsync<NgbArgumentRequiredException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ResolveAsync_WhenVariantCodeIsBlank_ReturnsExactRequestWithoutLookup(string? variantCode)
    {
        var service = new StubVariantService(null);
        var resolver = new ReportVariantRequestResolver(service);
        var request = new ReportExecutionRequestDto(VariantCode: variantCode);

        var result = await resolver.ResolveAsync("report", request, CancellationToken.None);

        result.Should().BeSameAs(request);
        service.GetCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ResolveAsync_WhenVariantDoesNotExist_ThrowsNotFound()
    {
        var resolver = new ReportVariantRequestResolver(new StubVariantService(null));

        var act = () => resolver.ResolveAsync(
            "report",
            new ReportExecutionRequestDto(VariantCode: "missing"),
            CancellationToken.None);

        await act.Should().ThrowAsync<ReportVariantNotFoundException>();
    }

    [Fact]
    public async Task ResolveAsync_WhenBaselineCollectionsAreNull_UsesRequestOverrides()
    {
        var variant = Variant(filters: null, parameters: null);
        var resolver = new ReportVariantRequestResolver(new StubVariantService(variant));
        var requestLayout = new ReportLayoutDto(ShowDetails: true);
        var requestFilters = new Dictionary<string, ReportFilterValueDto>
        {
            ["request"] = Filter("request")
        };
        var requestParameters = new Dictionary<string, string>
        {
            ["request"] = "request"
        };

        var result = await resolver.ResolveAsync(
            "report",
            new ReportExecutionRequestDto(
                Layout: requestLayout,
                Filters: requestFilters,
                Parameters: requestParameters,
                VariantCode: "saved",
                Offset: 7,
                Limit: 13,
                Cursor: "cursor",
                DisablePaging: true),
            CancellationToken.None);

        result.Layout.Should().BeSameAs(requestLayout);
        result.Filters.Should().BeSameAs(requestFilters);
        result.Parameters.Should().BeSameAs(requestParameters);
        result.VariantCode.Should().Be("saved");
        result.Offset.Should().Be(7);
        result.Limit.Should().Be(13);
        result.Cursor.Should().Be("cursor");
        result.DisablePaging.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAsync_WhenOverridesAreNull_UsesVariantBaselineAndLayout()
    {
        var variantLayout = new ReportLayoutDto(ShowGrandTotals: true);
        var baselineFilters = new Dictionary<string, ReportFilterValueDto>
        {
            ["baseline"] = Filter("baseline")
        };
        var baselineParameters = new Dictionary<string, string>
        {
            ["baseline"] = "baseline"
        };
        var variant = Variant(variantLayout, baselineFilters, baselineParameters);
        var resolver = new ReportVariantRequestResolver(new StubVariantService(variant));

        var result = await resolver.ResolveAsync(
            "report",
            new ReportExecutionRequestDto(VariantCode: "saved"),
            CancellationToken.None);

        result.Layout.Should().BeSameAs(variantLayout);
        result.Filters.Should().BeSameAs(baselineFilters);
        result.Parameters.Should().BeSameAs(baselineParameters);
    }

    [Fact]
    public async Task ResolveAsync_WhenCollectionsAreEmpty_UsesOppositeNonEmptyCollection()
    {
        var baselineFilters = new Dictionary<string, ReportFilterValueDto>
        {
            ["baseline"] = Filter("baseline")
        };
        var requestParameters = new Dictionary<string, string>
        {
            ["request"] = "request"
        };
        var variant = Variant(
            filters: baselineFilters,
            parameters: new Dictionary<string, string>());
        var resolver = new ReportVariantRequestResolver(new StubVariantService(variant));

        var result = await resolver.ResolveAsync(
            "report",
            new ReportExecutionRequestDto(
                Filters: new Dictionary<string, ReportFilterValueDto>(),
                Parameters: requestParameters,
                VariantCode: "saved"),
            CancellationToken.None);

        result.Filters.Should().BeSameAs(baselineFilters);
        result.Parameters.Should().BeSameAs(requestParameters);
    }

    [Fact]
    public async Task ResolveAsync_WhenBothCollectionsHaveValues_MergesCaseInsensitivelyWithRequestPrecedence()
    {
        var variant = Variant(
            filters: new Dictionary<string, ReportFilterValueDto>
            {
                ["shared"] = Filter("baseline"),
                ["baseline-only"] = Filter("baseline-only")
            },
            parameters: new Dictionary<string, string>
            {
                ["shared"] = "baseline",
                ["baseline-only"] = "baseline-only"
            });
        var resolver = new ReportVariantRequestResolver(new StubVariantService(variant));

        var result = await resolver.ResolveAsync(
            "report",
            new ReportExecutionRequestDto(
                Filters: new Dictionary<string, ReportFilterValueDto>
                {
                    ["SHARED"] = Filter("request"),
                    ["request-only"] = Filter("request-only")
                },
                Parameters: new Dictionary<string, string>
                {
                    ["SHARED"] = "request",
                    ["request-only"] = "request-only"
                },
                VariantCode: "saved"),
            CancellationToken.None);

        result.Filters.Should().HaveCount(3);
        result.Filters!["shared"].Value.GetString().Should().Be("request");
        result.Filters.Should().ContainKeys("baseline-only", "request-only");
        result.Parameters.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["shared"] = "request",
            ["baseline-only"] = "baseline-only",
            ["request-only"] = "request-only"
        });
    }

    private static ReportVariantDto Variant(
        ReportLayoutDto? layout = null,
        IReadOnlyDictionary<string, ReportFilterValueDto>? filters = null,
        IReadOnlyDictionary<string, string>? parameters = null)
        => new(
            VariantCode: "saved",
            ReportCode: "report",
            Name: "Saved",
            Layout: layout,
            Filters: filters,
            Parameters: parameters);

    private static ReportFilterValueDto Filter(string value)
        => new(JsonSerializer.SerializeToElement(value));

    private sealed class StubVariantService(ReportVariantDto? variant) : IReportVariantService
    {
        public int GetCallCount { get; private set; }

        public Task<IReadOnlyList<ReportVariantDto>> GetAllAsync(string reportCode, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<ReportVariantDto>>([]);

        public Task<ReportVariantDto?> GetAsync(string reportCode, string variantCode, CancellationToken ct)
        {
            GetCallCount++;
            return Task.FromResult(variant);
        }

        public Task<ReportVariantDto> SaveAsync(ReportVariantDto value, CancellationToken ct)
            => Task.FromResult(value);

        public Task DeleteAsync(string reportCode, string variantCode, CancellationToken ct)
            => Task.CompletedTask;
    }
}
