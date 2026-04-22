using FluentAssertions;
using NGB.Core.Dimensions;
using NGB.PostgreSql.Readers;
using Xunit;

namespace NGB.Runtime.IntegrationTests.PostgreSql;

public sealed class SqlDimensionFilter_Scopes_Normalize_P0Tests
{
    [Fact]
    public void NormalizeScopes_WhenEmpty_ReturnsZeroShape()
    {
        var result = SqlDimensionFilter.NormalizeScopes(null);

        result.ScopeDimensionCount.Should().Be(0);
        result.ScopeDimIds.Should().BeEmpty();
        result.ScopeValueIds.Should().BeEmpty();
    }

    [Fact]
    public void NormalizeScopes_WhenMultipleValuesPerDimension_FlattensPairs_And_UsesDistinctDimensionCount()
    {
        var dim1 = Guid.CreateVersion7();
        var dim2 = Guid.CreateVersion7();
        var a1 = Guid.CreateVersion7();
        var a2 = Guid.CreateVersion7();
        var b1 = Guid.CreateVersion7();

        var scopes = new DimensionScopeBag([
            new DimensionScope(dim1, [a1, a2]),
            new DimensionScope(dim2, [b1])
        ]);

        var result = SqlDimensionFilter.NormalizeScopes(scopes);
        var expectedPairs = scopes
            .SelectMany(scope => scope.ValueIds.Select(valueId => (scope.DimensionId, ValueId: valueId)))
            .ToArray();

        result.ScopeDimensionCount.Should().Be(2);
        result.ScopeDimIds.Should().Equal(expectedPairs.Select(x => x.DimensionId));
        result.ScopeValueIds.Should().Equal(expectedPairs.Select(x => x.ValueId));
    }
}
