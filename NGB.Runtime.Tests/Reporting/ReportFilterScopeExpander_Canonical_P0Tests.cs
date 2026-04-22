using System.Text.Json;
using FluentAssertions;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Core.Dimensions;
using NGB.Runtime.Reporting;
using NGB.Tools.Extensions;
using NGB.Tools.Normalization;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportFilterScopeExpander_Canonical_P0Tests
{
    [Fact]
    public async Task ExpandAsync_Canonical_Filter_Metadata_Expands_IncludeDescendants()
    {
        var propertyDimensionId = DeterministicGuid.Create($"Dimension|{CodeNormalizer.NormalizeCodeNorm("test.property", "dimensionCode")}");
        var originalId = Guid.Parse("00000000-0000-0000-0000-000000000101");
        var childId = Guid.Parse("00000000-0000-0000-0000-000000000102");

        var sut = new ReportFilterScopeExpander(
            new StubDimensionScopeExpansionService(new DimensionScopeBag([
                new DimensionScope(propertyDimensionId, [originalId, childId], includeDescendants: false)
            ])),
            new StubDimensionDefinitionReader(new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase)
            {
                ["test.property"] = propertyDimensionId
            }));

        var runtime = new ReportDefinitionRuntimeModel(new ReportDefinitionDto(
            ReportCode: "accounting.trial_balance",
            Name: "Trial Balance",
            Mode: ReportExecutionMode.Canonical,
            Filters:
            [
                new ReportFilterFieldDto(
                    "property_id",
                    "Property",
                    "uuid",
                    IsMulti: true,
                    SupportsIncludeDescendants: true,
                    DefaultIncludeDescendants: true,
                    Lookup: new CatalogLookupSourceDto("test.property"))
            ]));

        var expanded = await sut.ExpandAsync(
            runtime,
            new ReportExecutionRequestDto(
                Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                {
                    ["property_id"] = new(JsonSerializer.SerializeToElement(new[] { originalId }), IncludeDescendants: true)
                },
                Offset: 0,
                Limit: 50),
            CancellationToken.None);

        expanded.Filters.Should().NotBeNull();
        expanded.Filters!["property_id"].IncludeDescendants.Should().BeFalse();
        expanded.Filters["property_id"].Value.EnumerateArray().Select(x => x.GetGuid()).Should().Equal(originalId, childId);
    }

    private sealed class StubDimensionScopeExpansionService(DimensionScopeBag expanded) : IDimensionScopeExpansionService
    {
        public Task<DimensionScopeBag?> ExpandAsync(string reportCode, DimensionScopeBag? scopes, CancellationToken ct)
            => Task.FromResult<DimensionScopeBag?>(expanded);
    }

    private sealed class StubDimensionDefinitionReader(IReadOnlyDictionary<string, Guid> idsByCode) : IDimensionDefinitionReader
    {
        public Task<IReadOnlyDictionary<string, Guid>> GetDimensionIdsByCodesAsync(IReadOnlyCollection<string> dimensionCodes, CancellationToken ct)
            => Task.FromResult(idsByCode);
    }
}
