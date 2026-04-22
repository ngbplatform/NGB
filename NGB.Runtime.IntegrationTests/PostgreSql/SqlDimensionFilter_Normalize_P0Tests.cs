using FluentAssertions;
using NGB.Core.Dimensions;
using NGB.PostgreSql.Readers;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.PostgreSql;

public sealed class SqlDimensionFilter_Normalize_P0Tests
{
    [Fact]
    public void Normalize_WhenNull_ReturnsEmptyArraysAndZeroCount()
    {
        var (dimIds, valueIds, count) = SqlDimensionFilter.Normalize(null);

        dimIds.Should().BeEmpty();
        valueIds.Should().BeEmpty();
        count.Should().Be(0);
    }

    [Fact]
    public void Normalize_WhenEmpty_ReturnsEmptyArraysAndZeroCount()
    {
        var (dimIds, valueIds, count) = SqlDimensionFilter.Normalize(Array.Empty<DimensionValue>());

        dimIds.Should().BeEmpty();
        valueIds.Should().BeEmpty();
        count.Should().Be(0);
    }

    [Fact]
    public void Normalize_WhenDuplicateDimensionId_Throws()
    {
        var dimId = Guid.CreateVersion7();

        var filter = new[]
        {
            new DimensionValue(dimId, Guid.CreateVersion7()),
            new DimensionValue(dimId, Guid.CreateVersion7()),
        };

        var act = () => SqlDimensionFilter.Normalize(filter);

        act.Should().Throw<NgbArgumentInvalidException>()
            .WithMessage("*duplicate dimension id*");
    }

    [Fact]
    public void Normalize_WhenValid_ProducesParallelArraysAndCount()
    {
        var d1 = new DimensionValue(Guid.CreateVersion7(), Guid.CreateVersion7());
        var d2 = new DimensionValue(Guid.CreateVersion7(), Guid.CreateVersion7());

        var (dimIds, valueIds, count) = SqlDimensionFilter.Normalize(new[] { d1, d2 });

        count.Should().Be(2);

        dimIds.Should().Equal(d1.DimensionId, d2.DimensionId);
        valueIds.Should().Equal(d1.ValueId, d2.ValueId);
    }
}
