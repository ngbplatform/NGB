using FluentAssertions;
using NGB.Core.Dimensions;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Dimensions;

public sealed class DimensionScopeBag_P0Tests
{
    [Fact]
    public void DimensionScope_Deduplicates_And_Sorts_ValueIds()
    {
        var dimensionId = Guid.CreateVersion7();
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();

        var scope = new DimensionScope(dimensionId, [b, a, b], includeDescendants: true);

        scope.DimensionId.Should().Be(dimensionId);
        scope.IncludeDescendants.Should().BeTrue();
        scope.ValueIds.Should().Equal(new[] { a, b }.OrderBy(x => x));
    }

    [Fact]
    public void DimensionScope_WhenEmptyValueList_Throws()
    {
        var act = () => new DimensionScope(Guid.CreateVersion7(), Array.Empty<Guid>());

        act.Should().Throw<NgbArgumentInvalidException>()
            .WithMessage("*At least one valueId is required*");
    }

    [Fact]
    public void DimensionScopeBag_WhenDuplicateDimensionId_Throws()
    {
        var dimensionId = Guid.CreateVersion7();
        var first = new DimensionScope(dimensionId, [Guid.CreateVersion7()]);
        var second = new DimensionScope(dimensionId, [Guid.CreateVersion7()], includeDescendants: true);

        var act = () => new DimensionScopeBag([first, second]);

        act.Should().Throw<NgbArgumentInvalidException>()
            .WithMessage("*Duplicate DimensionId*");
    }

    [Fact]
    public void DimensionScopeBag_Canonicalizes_Order_By_DimensionId()
    {
        var d1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var d2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var first = new DimensionScope(d2, [Guid.CreateVersion7()]);
        var second = new DimensionScope(d1, [Guid.CreateVersion7()]);

        var bag = new DimensionScopeBag([first, second]);

        bag.Select(x => x.DimensionId).Should().Equal(d1, d2);
    }
}
